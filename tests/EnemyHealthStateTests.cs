using System;

public static class EnemyHealthStateTests
{
    static void Check(bool value,string message) { if(!value) throw new Exception(message); }
    public static void Main()
    {
        var state=new NativeEnemyHealthState(); state.Spawn(100);
        Check(!state.Damaged && state.Ratio==1,"Full enemy starts hidden");
        Check(state.Hit(100,100,21)==0 && !state.Damaged,"Blocked hit creates no number");
        Check(state.Hit(100,79,21)==21 && state.Damaged && state.Maximum==100,"Successful normal hit");
        Check(Math.Abs(state.Ratio-.79f)<.001f,"HP ratio follows actual game HP");
        Check(state.Hit(79,78,1)==1,"Game damageOverride remains one damage");
        Check(state.Hit(78,-50,9999)==9999 && state.Ratio==0,"Killing damage retains its effective number");
        Check(state.Hit(-50,-51,1)==0,"Already dead enemy ignored");
        state.Spawn(40); Check(state.Maximum==40 && !state.Damaged,"Pooled spawn resets old maximum and hit state");
        state.Observe(120); Check(state.Maximum==120 && state.Ratio==1,"Late Godhome HP scaling");
        state.Hit(120,110,10); state.Observe(115); Check(state.Maximum==120,"Healing does not shrink maximum");
        Check(state.Hit(115,120,10)==0,"Healing never creates damage");
        Console.WriteLine("ENEMY_HEALTH_STATE_TESTS_PASS");
    }
}
