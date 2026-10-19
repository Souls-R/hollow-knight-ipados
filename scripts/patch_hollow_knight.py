"""Version-checked compatibility changes to generated C++; game IL stays original."""
import re
from patch_original_display import apply as display

def apply(cpp):
    matches=[]
    pattern=re.compile(r'IL2CPP_EXTERN_C IL2CPP_METHOD_ATTR void InputHandler_SetActiveGamepadType_[^(]+\([^\n]+\)\s*\n\{')
    for path in cpp.glob('Assembly-CSharp*.cpp'):
        source=path.read_text()
        for match in pattern.finditer(source):matches.append((path,source,match))
    assert len(matches)==1
    path,source,match=matches[0]
    end=source.index('\n}',match.end())+2
    body=source[match.start():end]
    old='if ((((int32_t)L_1) == ((int32_t)2)))'
    assert body.count(old)==1 and 'InputDevice_get_DeviceStyle_' in body
    assert re.search(r'L_1 = Application_get_platform_\w+\(NULL\);\s*'+re.escape(old),body)
    body=body.replace(old,'if ((((int32_t)L_1) == ((int32_t)2)) || (((int32_t)L_1) == ((int32_t)8)))')
    path.write_text(source[:match.start()]+body+source[end:])
    return dict(controller_platform='IPhonePlayer uses original device-style bindings (Xbox View/Menu)',original_display_menu=display(cpp),original_game_il_unchanged=True)
