using Microsoft.AspNetCore.Identity.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore;

namespace DiceGame.Data;


public class MatchRecord
{
    public int Id { get; set; }
    public string UserId1 { get; set; } = default!;
    public string UserId2 { get; set; } = default!;
    public int Score1 { get; set; }
    public int Score2 { get; set; }
    public int Winner { get; set; }
    public DateTime When { get; set; }

    public int GetUserScore(string? userId)
    {
        if (UserId1 == userId) return Score1;
        if (UserId2 == userId) return Score2;
        return 0;
    }

    public string? WinnerUserId
    {
        get
        {
            if (Winner == 0) return UserId1;
            else if (Winner == 1) return UserId2;
            else return null;
        }
    }

    public int WinnerScore
    {
        get
        {
            if (Winner == 0) return Score1;
            else if (Winner == 1) return Score2;
            else return 0;
        }
    }
}


public class ApplicationDbContext(DbContextOptions<ApplicationDbContext> options) : IdentityDbContext<ApplicationUser>(options)
{
    public DbSet<MatchRecord> MatchRecords { get; set; }
}
