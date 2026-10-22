from pathlib import Path
import tempfile
import unittest
from scripts.patch_enemy_hud import apply

HEADER='IL2CPP_EXTERN_C IL2CPP_METHOD_ATTR void '
HEALTH='HealthManager_tABCDEF1234'

def fixture(directory,early=False):
    source=(HEADER+'HealthManager_OnEnable_mAAAA ('+HEALTH+'* __this, const RuntimeMethod* method)\n{\n\tStartCoroutine();\n\treturn;\n}\n'+
        HEADER+'HealthManager_SubtractHealth_mBBBB ('+HEALTH+'* __this, int32_t ___0_damageDealt, const RuntimeMethod* method)\n{\n'+
        ('\tif (blocked) return;\n' if early else '')+
        '\tif (overrideDamage) ___0_damageDealt = 1;\n\t__this->___hp -= ___0_damageDealt;\n\treturn;\n}\n')
    (directory/'Assembly-CSharp.cpp').write_text(source)
    (directory/'NativeCompatibility.cpp').write_text(
        HEADER+'NativeEnemyHud_Spawned_mCCCC ('+HEALTH+'* health, const RuntimeMethod* method)\n{\n}\n'+
        HEADER+'NativeEnemyHud_Damaged_mDDDD ('+HEALTH+'* health, int32_t before, int32_t damage, const RuntimeMethod* method)\n{\n}\n')
    return source

class EnemyHudHookTests(unittest.TestCase):
    def test_observes_after_subtraction_using_effective_override(self):
        with tempfile.TemporaryDirectory() as tmp:
            root=Path(tmp); original=fixture(root)
            apply(root)
            result=(root/'Assembly-CSharp.cpp').read_text()
            self.assertLess(result.index('hkPreviousHP = __this->___hp'),result.index('if (overrideDamage)'))
            self.assertLess(result.index('__this->___hp -='),result.index('NativeEnemyHud_Damaged_mDDDD(__this'))
            self.assertIn('hkPreviousHP, ___0_damageDealt, NULL',result)
            self.assertIn('if (overrideDamage) ___0_damageDealt = 1;',result)
            self.assertIn('__this->___hp -= ___0_damageDealt;',result)
            with self.assertRaises(AssertionError): apply(root)

    def test_rejects_changed_damage_control_flow_without_modifying_files(self):
        with tempfile.TemporaryDirectory() as tmp:
            root=Path(tmp); original=fixture(root,early=True)
            with self.assertRaises(AssertionError): apply(root)
            self.assertEqual((root/'Assembly-CSharp.cpp').read_text(),original)

if __name__=='__main__': unittest.main()
