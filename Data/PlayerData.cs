using System.Threading;
using System.Collections.Concurrent;
using Microsoft.AspNetCore.Identity;

namespace DiceGame.Data;

using UserId = string;


public class Room
{
    private ApplicationUser[] users = new ApplicationUser[2];

    public Room(ApplicationUser user0, ApplicationUser user1)
    {
        users[0] = user0;
        users[1] = user1;
    }

    public ApplicationUser GetUser(int index)
    {
        return users[index];
    }
}


public class PlayerData
{
    private UserId? waitingUserId;
    private ConcurrentDictionary<UserId, Room> rooms = new ConcurrentDictionary<UserId, Room>();

    public event EventHandler? UserRoomChange;

    protected void OnUserRoomChange()
    => UserRoomChange?.Invoke(this, EventArgs.Empty);


    public Room? GetUserRoom(UserId userId)
    {
        Room? room;
        rooms.TryGetValue(userId, out room);
        return room;
    }


    public async void StartMatchmaking(UserId userId, UserManager<ApplicationUser> userManager)
    {
        if (GetUserRoom(userId) is not null) return;

        UserId? otherUserId = null;

        while (otherUserId is null)
        {
            // pop waiting user
            otherUserId = Interlocked.Exchange(ref waitingUserId, null);

            if (otherUserId is null)
            {
                // try putting us as the waiting user
                if (Interlocked.CompareExchange(ref waitingUserId, userId, null) is null) return;
            }
        }

        // we got two userIds!
        var user = await userManager.FindByIdAsync(userId);
        var otherUser = await userManager.FindByIdAsync(otherUserId);

        if (user is not null && otherUser is not null)
        {
            lock(this)
            {
                var room = new Room(user, otherUser);
                if (!rooms.TryAdd(userId, room)) return;
                if (!rooms.TryAdd(otherUserId, room))
                {
                    rooms.TryRemove(userId, out room);
                    return;
                }
            }

            OnUserRoomChange();
        }
    }


    public void StopMatchmaking(UserId userId)
    {
        Interlocked.CompareExchange(ref waitingUserId, null, userId);
    }
}
