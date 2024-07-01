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
    protected class UserData
    {
        public Room? room;

        public event EventHandler? UserRoomChange;
        public void OnUserRoomChange()
        => UserRoomChange?.Invoke(this, EventArgs.Empty);
    }


    private UserId? waitingUserId;
    private ConcurrentDictionary<UserId, UserData> users = new ConcurrentDictionary<UserId, UserData>();
    private ConcurrentDictionary<int, Room> rooms = new ConcurrentDictionary<int, Room>();


    public Room? GetUserRoom(UserId userId)
    {
        UserData? ud;
        users.TryGetValue(userId, out ud);
        return ud?.room;
    }


    public Room? GetRoom(int id)
    {
        Room? room;
        rooms.TryGetValue(id, out room);
        return room;
    }


    public void RegisterUserRoomChangeHandler(UserId userId, EventHandler eh)
    {
        var ud = users.GetOrAdd(userId, UserDataFactory);
        ud.UserRoomChange += eh;
    }


    public void UnregisterUserRoomChangeHandler(UserId userId, EventHandler eh)
    {
        var ud = users.GetOrAdd(userId, UserDataFactory);
        ud.UserRoomChange -= eh;
    }


    private UserData UserDataFactory(UserId id) => new UserData();


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
            // lock user changing rooms
            UserData? ud1, ud2;

            lock(this)
            {
                // check if the two users still has no room
                if (GetUserRoom(userId) is not null) return;
                if (GetUserRoom(otherUserId) is not null) return;

                // make a new room
                var room = new Room(user, otherUser);

                var rng = Random.Shared;
                do {
                    room.SetId(rng.Next());
                } while(!rooms.TryAdd(room.Id, room));

                ud1 = users.GetOrAdd(userId, UserDataFactory);
                ud2 = users.GetOrAdd(otherUserId, UserDataFactory);
                ud1.room = room;
                ud2.room = room;
            }

            ud1.OnUserRoomChange();
            ud2.OnUserRoomChange();
        }
    }


    public void StopMatchmaking(UserId userId)
    {
        Interlocked.CompareExchange(ref waitingUserId, null, userId);
    }
}
