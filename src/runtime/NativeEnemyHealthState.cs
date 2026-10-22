using System;

// Observation only: no changes to game HP, invulnerability or damage scaling.
public sealed class NativeEnemyHealthState
{
    public int Maximum { get; private set; }
    public int Current { get; private set; }
    public bool Damaged { get; private set; }
    public float Ratio { get { return Maximum<=0?0:Math.Max(0,Math.Min(1,(float)Current/Maximum)); } }
    public void Spawn(int hp) { Maximum=Math.Max(0,hp); Current=hp; Damaged=false; }
    public void Observe(int hp) { Maximum=Math.Max(Maximum,hp); Current=hp; }
    public int Hit(int before,int after,int effectiveDamage)
    {
        Maximum=Math.Max(Maximum,before); Current=after;
        if (before<=0 || after>=before || effectiveDamage<=0) return 0;
        Damaged=true;
        return effectiveDamage;
    }
}
