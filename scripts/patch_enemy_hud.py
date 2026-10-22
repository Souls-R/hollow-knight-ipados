"""Observe native health lifecycle and actual HP subtraction, without detours."""
from pathlib import Path
import re

METHOD=re.compile(r'^IL2CPP_EXTERN_C IL2CPP_METHOD_ATTR [^\n]+? (\w+) \([^\n]+\)\s*\n\{',re.M)
MARKER='// HK_NATIVE_ENEMY_HUD'

def apply(cpp):
    cpp=Path(cpp)
    wanted={'HealthManager_OnEnable','HealthManager_SubtractHealth','NativeEnemyHud_Spawned','NativeEnemyHud_Damaged'}
    found={}
    for path in [*cpp.glob('Assembly-CSharp*.cpp'),*cpp.glob('NativeCompatibility*.cpp')]:
        source=path.read_text(encoding='utf-8')
        for match in METHOD.finditer(source):
            name=match[1].rsplit('_m',1)[0]
            if name in wanted:
                assert name not in found,name
                end=source.index('\n}',match.end())+2
                found[name]=(path,match[1],source[match.start():end])
    assert set(found)==wanted,wanted-set(found)
    spawn=found['NativeEnemyHud_Spawned'][1]
    damage=found['NativeEnemyHud_Damaged'][1]
    grouped={}
    for name,callback in [('HealthManager_OnEnable',spawn),('HealthManager_SubtractHealth',damage)]:
        path,method,body=found[name]
        assert MARKER not in path.read_text(encoding='utf-8')
        health=re.search(r'\(\s*(HealthManager_t[0-9A-F]+)\* __this',body)[1]
        if name=='HealthManager_SubtractHealth':
            assert '___hp' in body and '___0_damageDealt' in body
            # This particular game has a single linear subtraction path. Fail
            # rather than silently hooking a changed method with early exits.
            assert len(re.findall(r'\breturn\s*;',body))==1
            new=body.replace('\n{','\n{\n int32_t hkPreviousHP = __this->___hp;',1)
            pos=new.rfind('return;')
            new=new[:pos]+f'{callback}(__this, hkPreviousHP, ___0_damageDealt, NULL);\n\t\t'+new[pos:]
            declaration=f'IL2CPP_EXTERN_C IL2CPP_METHOD_ATTR void {callback}({health}*, int32_t, int32_t, const RuntimeMethod*);'
        else:
            assert len(re.findall(r'\breturn\s*;',body))==1
            pos=body.rfind('return;')
            new=body[:pos]+f'{callback}(__this, NULL);\n\t\t'+body[pos:]
            declaration=f'IL2CPP_EXTERN_C IL2CPP_METHOD_ATTR void {callback}({health}*, const RuntimeMethod*);'
        grouped.setdefault(path,[]).append((body,new,declaration))
    for path,edits in grouped.items():
        source=path.read_text(encoding='utf-8')
        declarations='\n'.join(e[2] for e in edits)
        for old,new,_ in edits:
            assert source.count(old)==1
            source=source.replace(old,new)
        position=METHOD.search(source).start()
        source=source[:position]+MARKER+'\n'+declarations+'\n'+source[position:]
        path.write_text(source,encoding='utf-8')
    return dict(hooks=['HealthManager.OnEnable','HealthManager.SubtractHealth'],game_damage_unchanged=True)
