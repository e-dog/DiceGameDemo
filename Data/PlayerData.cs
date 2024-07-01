using System.Threading;
using System.Collections.Concurrent;
using Microsoft.AspNetCore.Identity;

namespace DiceGame.Data;

using UserId = string;


public class Room
{
    public int Id { get { return _id; } }

    public ApplicationUser GetUser(int index)
    {
        return users[index];
    }


    // game stuff
    public int[] Scores = new int[2];
    public int Step;

    public int Turn { get { return Step/2; } }
    public int Side { get { return Step%2; } }

    public string[] Rolls = new string[2];

    public int Winner;
    public bool GameOver;


    public event EventHandler? RoomChanged;
    public void OnRoomChanged()
    => RoomChanged?.Invoke(this, EventArgs.Empty);


    public void ResetGame()
    {
        for (int i=0; i<2; i++)
        {
            Scores[i] = 0;
            Rolls[i] = "";
        }

        Step = 0;
        Winner = -1;
        GameOver = false;
    }


    // private stuff
    public Room(ApplicationUser user0, ApplicationUser user1)
    {
        users[0] = user0;
        users[1] = user1;
        ResetGame();
    }

    public void SetId(int id) { _id = id; }

    private int _id = -1;
    private ApplicationUser[] users = new ApplicationUser[2];
}


public class PlayerData
{
    private UserId? waitingUserId;
    private ConcurrentDictionary<UserId, Room> userRooms = new ConcurrentDictionary<UserId, Room>();
    private ConcurrentDictionary<int, Room> rooms = new ConcurrentDictionary<int, Room>();

    public event EventHandler? UserRoomChange;

    protected void OnUserRoomChange()
    => UserRoomChange?.Invoke(this, EventArgs.Empty);


    public Room? GetUserRoom(UserId userId)
    {
        Room? room;
        userRooms.TryGetValue(userId, out room);
        return room;
    }


    public Room? GetRoom(int id)
    {
        Room? room;
        rooms.TryGetValue(id, out room);
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
                if (!userRooms.TryAdd(userId, room)) return;
                if (!userRooms.TryAdd(otherUserId, room))
                {
                    userRooms.TryRemove(userId, out room);
                    return;
                }

                var rng = Random.Shared;
                do {
                    room.SetId(rng.Next());
                } while(!rooms.TryAdd(room.Id, room));
            }

            OnUserRoomChange();
        }
    }


    public void StopMatchmaking(UserId userId)
    {
        Interlocked.CompareExchange(ref waitingUserId, null, userId);
    }
}
