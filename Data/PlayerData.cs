using System.Threading;
using System.Collections.Concurrent;
using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;

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

    public int Rematch;


    public DateTime TimeOutTime { get { return _timeOut; } }
    public double TimeOutSeconds { get { return (_timeOut - DateTime.UtcNow).TotalSeconds; } }
    public bool TimedOut { get { return _timedOut; } }

    public void ResetTimeOut()
    {
        if (!GameOver)
        {
            _timeOut = DateTime.UtcNow + new TimeSpan(0, 0, 30);

            if (_timeOutTimer is null)
            {
                var timer = new System.Threading.Timer(OnTimer, null, 1000, 500);
                Interlocked.CompareExchange(ref _timeOutTimer, timer, null)?.Dispose();
            }
        }
        else
        {
            Interlocked.Exchange(ref _timeOutTimer, null)?.Dispose();
        }
    }


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
        Rematch = 0;
        _timedOut = false;
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

    private DateTime _timeOut;
    private System.Threading.Timer? _timeOutTimer;
    private bool _timedOut;

    private void OnTimer(object? state)
    {
        if (!GameOver)
        {
            if (DateTime.UtcNow > _timeOut)
            {
                GameOver = true;
                _timedOut = true;
                Step++;
                Winner = Side;
            }

            OnRoomChanged();
        }

        if (GameOver)
        {
            Interlocked.Exchange(ref _timeOutTimer, null)?.Dispose();
        }
    }
}


public class PlayerData(IDbContextFactory<ApplicationDbContext> _DbFactory)
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


    public void RemoveRoom(int id)
    {
        Room? room;
        rooms.TryGetValue(id, out room);
        if (room is null) return;

        // lock user changing rooms
        lock(this)
        {
            Room? r;
            rooms.TryRemove(id, out r);

            for (int i=0; i<2; i++)
            {
                var ud = users.GetOrAdd(room.GetUser(i).Id, UserDataFactory);
                ud.room = null;
            }
        }

        // notify users
        for (int i=0; i<2; i++)
        {
            users.GetOrAdd(room.GetUser(i).Id, UserDataFactory).OnUserRoomChange();
        }

        // record to db
        using (var context = _DbFactory.CreateDbContext())
        {
            context.Add(new MatchRecord
            {
                UserId1 = room.GetUser(0).Id,
                UserId2 = room.GetUser(1).Id,
                Score1 = room.Scores[0],
                Score2 = room.Scores[1],
                Winner = room.Winner,
                When = DateTime.UtcNow,
            });

            context.SaveChangesAsync();
        }
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
            UserData? ud1, ud2;

            // lock user changing rooms
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

                room.ResetTimeOut();
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
