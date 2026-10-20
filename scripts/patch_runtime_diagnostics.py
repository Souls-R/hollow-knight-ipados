"""Bounded, opt-in diagnostics in generated code; no new managed metadata.

Keep original movie/input/menu control flow. Only log errors that the original
movie constructor discarded, and temporarily request frame callbacks when
diagnostic logging is enabled. Restore the original callback flag after sampling.
"""
from pathlib import Path
import hashlib
import json
import re
import time

METHOD = re.compile(r'^IL2CPP_EXTERN_C IL2CPP_METHOD_ATTR [^\n]+? (\w+) \([^\n]+\)\s*\n\{', re.M)
MARKER = '// HK_DIAGNOSTICS_021'

def apply(cpp):
    backup_root = cpp.parent/('diagnostics-before-'+str(time.time_ns()))
    pool = list(cpp.glob('UnityEngine.CoreModule*.cpp')) + list(cpp.glob('Unity.InputSystem*.cpp')) + list(cpp.glob('Assembly-CSharp*.cpp')) + [cpp/'NativeCompatibility.cpp', cpp/'UnityEngine.VideoModule.cpp', cpp/'TeamCherry.Cinematics.cpp']
    wanted = set('''NativePreferences_get_CompatibilityLogs Debug_Log Debug_LogError Debug_LogException
Gamepad_get_current Gamepad_get_startButton Gamepad_get_selectButton ButtonControl_get_wasPressedThisFrame ButtonControl_get_wasReleasedThisFrame OneAxisInputControl_get_WasPressed
ForceCameraAspect_get_CurrentViewportAspect ForceCameraAspect_get_CurrentMainCamHeightMult
VideoPlayer_get_url VideoPlayer_get_width VideoPlayer_get_height VideoPlayer_get_frame VideoPlayer_get_time VideoPlayer_get_isPrepared VideoPlayer_get_isPlaying VideoPlayer_get_frameRate VideoPlayer_get_renderMode VideoPlayer_get_targetMaterialRenderer VideoPlayer_get_targetMaterialProperty VideoPlayer_get_texture VideoPlayer_get_sendFrameReadyEvents VideoPlayer_set_sendFrameReadyEvents
Renderer_get_sharedMaterial Renderer_get_enabled Renderer_get_isVisible Material_get_shader Shader_get_isSupported Object_get_name'''.split())
    methods = {}
    for path in pool:
        source = path.read_text()
        for match in METHOD.finditer(source):
            simple = match[1].rsplit('_m',1)[0]
            if simple in wanted:
                decl = match[0].rstrip()[:-1].rstrip()+';'
                # Select the single-argument Debug overloads.
                if simple in ('Debug_Log','Debug_LogError') and 'context' in decl: continue
                if simple == 'Debug_LogException' and 'context' in decl: continue
                methods.setdefault(simple, (match[1], decl))
    assert set(methods)==wanted, wanted-set(methods)
    def call(key,args=''):
        return methods[key][0]+'('+args+(', ' if args else '')+'NULL)'
    def declarations(keys):
        decls='\n'.join(methods[key][1] for key in keys)
        types=sorted(set(re.findall(r'\b(\w+_t[0-9A-F]{8,})\b',decls)))
        return '\n'.join('struct '+name+';' for name in types)+'\n'+decls+'\n'
    report=[]
    def edit(file, changes, header='', includes=''):
        path=cpp/file; original=path.read_text()
        assert MARKER not in original, file+' already instrumented'
        source=original.lstrip('\ufeff')
        edits=[]
        counts={key:0 for key in changes}
        for match in METHOD.finditer(source):
            simple=match[1].rsplit('_m',1)[0]
            if simple not in changes: continue
            end=source.index('\n}',match.end())+2
            body=source[match.start():end]
            new=changes[simple](body)
            assert new!=body, simple
            edits.append((match.start(),end,new));counts[simple]+=1
        assert all(n==1 for n in counts.values()), counts
        for start,end,new in reversed(edits):source=source[:start]+new+source[end:]
        position=METHOD.search(source).start()
        source=includes+source[:position]+MARKER+'\n'+header+source[position:]
        backup=backup_root/file
        backup.parent.mkdir(exist_ok=True)
        assert not backup.exists()
        backup.write_text(original)
        path.write_text(source)
        report.append(dict(file=file,before=hashlib.sha256(original.encode()).hexdigest(),after=hashlib.sha256(source.encode()).hexdigest(),hooks=counts))
    def prefix(text):
        return lambda body:body.replace('\n{','\n{\n'+text,1)

    # Same binary methods and metadata as 0.2.0; these are native-only helpers.
    native_keys=['NativePreferences_get_CompatibilityLogs','Debug_Log','Debug_LogError','Gamepad_get_current','Gamepad_get_startButton','Gamepad_get_selectButton','ButtonControl_get_wasPressedThisFrame','ButtonControl_get_wasReleasedThisFrame','OneAxisInputControl_get_WasPressed','ForceCameraAspect_get_CurrentViewportAspect','ForceCameraAspect_get_CurrentMainCamHeightMult']
    native_header=declarations(native_keys)+'''
extern "C" void HKDiagnosticMessage(const char* message)
{
    if (!FLAGS()) return;
    LOG((RuntimeObject*)il2cpp_codegen_string_new_wrapper(message), NULL);
}
extern "C" void HKDiagnosticError(const char* message)
{ ERROR((RuntimeObject*)il2cpp_codegen_string_new_wrapper(message), NULL); }
static void HKInputProbe()
{
    if (!FLAGS()) return;
    auto pad = PAD(); if (!pad) return;
    auto menu = MENU(pad, NULL); auto view = VIEW(pad, NULL);
    bool mp = menu && PRESS(menu, NULL), mr = menu && RELEASE(menu, NULL);
    bool vp = view && PRESS(view, NULL), vr = view && RELEASE(view, NULL);
    if (!(mp || mr || vp || vr)) return;
    il2cpp_codegen_initialize_runtime_metadata((uintptr_t*)&InputHandler_tB7F76670D49DAB5D76D0D1193F3A30EF4AF86710_il2cpp_TypeInfo_var);
    auto fields=(InputHandler_tB7F76670D49DAB5D76D0D1193F3A30EF4AF86710_StaticFields*)il2cpp_codegen_static_fields_for(InputHandler_tB7F76670D49DAB5D76D0D1193F3A30EF4AF86710_il2cpp_TypeInfo_var);
    auto input=fields ? fields->___Instance : nullptr;
    auto actions=input ? input->___inputActions : nullptr;
    std::ostringstream line;
    line << "HK_BUTTON: menuDown=" << mp << "; menuUp=" << mr << "; viewDown=" << vp << "; viewUp=" << vr;
    if(input) line << "; gamepadType=" << input->___activeGamepadType << "; deviceStyle=" << input->___lastInputDeviceStyle;
    if(actions) line << "; pause=" << (actions->___pause && ACTION((OneAxisInputControl_t2C59101D0C702B54647B671A722D66F1449A8FD9*)actions->___pause,NULL))
        << "; inventory=" << (actions->___openInventory && ACTION((OneAxisInputControl_t2C59101D0C702B54647B671A722D66F1449A8FD9*)actions->___openInventory,NULL))
        << "; quickMap=" << (actions->___quickMap && ACTION((OneAxisInputControl_t2C59101D0C702B54647B671A722D66F1449A8FD9*)actions->___quickMap,NULL));
    HKDiagnosticMessage(line.str().c_str());
}
static void HKAspectProbe()
{
    std::ostringstream line; line << "HK_ASPECT: viewport=" << ASPECT() << "; cameraHeightMultiplier=" << HEIGHT();
    HKDiagnosticMessage(line.str().c_str());
}
'''
    replacements={'FLAGS()':call('NativePreferences_get_CompatibilityLogs'),'LOG':methods['Debug_Log'][0],'ERROR':methods['Debug_LogError'][0],'PAD()':call('Gamepad_get_current'),'MENU':methods['Gamepad_get_startButton'][0],'VIEW':methods['Gamepad_get_selectButton'][0],'PRESS':methods['ButtonControl_get_wasPressedThisFrame'][0],'RELEASE':methods['ButtonControl_get_wasReleasedThisFrame'][0],'ACTION':methods['OneAxisInputControl_get_WasPressed'][0],'ASPECT()':call('ForceCameraAspect_get_CurrentViewportAspect'),'HEIGHT()':call('ForceCameraAspect_get_CurrentMainCamHeightMult')}
    for key,value in replacements.items():native_header=native_header.replace(key,value)
    def perf(body):
        needle=methods['Debug_Log'][0]+'('
        assert body.count(needle)==1
        return body.replace(needle,'HKAspectProbe();\n\t\t'+needle,1)
    edit('NativeCompatibility.cpp',{'NativeAnimatorCompatibility_LateUpdate':prefix('    HKInputProbe();'),'NativePerformanceSampler_LateUpdate':perf},native_header,'#include <sstream>\n')

    video_keys=[key for key in wanted if key.startswith(('VideoPlayer_','Renderer_','Material_','Shader_','Object_'))]+['NativePreferences_get_CompatibilityLogs','Debug_LogException']
    player_type=re.search(r'(VideoPlayer_t\w+)\*',methods['VideoPlayer_get_url'][1])[1]
    video_header=declarations(video_keys)+f'''
extern "C" void HKDiagnosticMessage(const char*);
extern "C" void HKDiagnosticError(const char*);
struct HKFrameProbe {{ int frames; bool previous; }};
static std::unordered_map<{player_type}*, HKFrameProbe> HKFrameProbes;
static std::string HKUtf8(String_t* text)
{{
    if(!text) return "<null>";
    char* native=il2cpp_codegen_marshal_string(text);
    std::string result=native ? native : "<null>";
    il2cpp_codegen_marshal_free(native);
    return result;
}}
extern "C" void HKVideoProbe(RuntimeObject* raw, const char* stage)
{{
    if (!{call('NativePreferences_get_CompatibilityLogs')}) return;
    auto player=({player_type}*)raw;
    try {{
        std::ostringstream line; line << "HK_VIDEO: " << stage << "; player=" << raw;
        if(player) {{
            auto url={call('VideoPlayer_get_url','player')};
            line << "; url=" << HKUtf8(url)
                << "; prepared=" << {call('VideoPlayer_get_isPrepared','player')}
                << "; playing=" << {call('VideoPlayer_get_isPlaying','player')}
                << "; time=" << {call('VideoPlayer_get_time','player')}
                << "; frame=" << {call('VideoPlayer_get_frame','player')}
                << "; fps=" << {call('VideoPlayer_get_frameRate','player')}
                << "; size=" << {call('VideoPlayer_get_width','player')} << "x" << {call('VideoPlayer_get_height','player')}
                << "; renderMode=" << {call('VideoPlayer_get_renderMode','player')}
                << "; texture=" << (void*){call('VideoPlayer_get_texture','player')};
            auto renderer={call('VideoPlayer_get_targetMaterialRenderer','player')};
            if(renderer) {{
                line << "; rendererEnabled=" << {call('Renderer_get_enabled','renderer')} << "; rendererVisible=" << {call('Renderer_get_isVisible','renderer')};
                auto material={call('Renderer_get_sharedMaterial','renderer')};
                auto shader=material ? {call('Material_get_shader','material')} : nullptr;
                if(shader) {{
                    auto name={call('Object_get_name','(Object_tC12DECB6760A7F2CBF65D9DCF18D044C2D97152C*)shader')};
                    line << "; shader=" << HKUtf8(name) << "; shaderSupported=" << {call('Shader_get_isSupported','shader')};
                }}
            }}
        }}
        HKDiagnosticMessage(line.str().c_str());
    }} catch(Il2CppExceptionWrapper& error) {{ {call('Debug_LogException','(Exception_t*)error.ex')}; }}
}}
static void HKArmFrames({player_type}* player)
{{
    if (!{call('NativePreferences_get_CompatibilityLogs')}) return;
    HKFrameProbes[player]={{0,{call('VideoPlayer_get_sendFrameReadyEvents','player')}}};
    {call('VideoPlayer_set_sendFrameReadyEvents','player, true')};
}}
static void HKSampleFrame({player_type}* player)
{{
    auto found=HKFrameProbes.find(player); if(found==HKFrameProbes.end()) return;
    int count=++found->second.frames;
    if(count==1 || count==30 || count==60) HKVideoProbe((RuntimeObject*)player, count==1 ? "first-frame" : "frame-sample");
    if(count>=60) {{
        if(!found->second.previous && !player->___frameReady) {call('VideoPlayer_set_sendFrameReadyEvents','player, false')};
        HKFrameProbes.erase(found);
    }}
}}
'''
    changes={
      'VideoPlayer_Prepare':prefix('    HKVideoProbe((RuntimeObject*)__this,"prepare-request"); HKArmFrames(__this);'),
      'VideoPlayer_InvokePrepareCompletedCallback_Internal':prefix('    HKVideoProbe((RuntimeObject*)___0_source,"prepared");'),
      'VideoPlayer_InvokeStartedCallback_Internal':prefix('    HKVideoProbe((RuntimeObject*)___0_source,"started");'),
      'VideoPlayer_InvokeSeekCompletedCallback_Internal':prefix('    HKVideoProbe((RuntimeObject*)___0_source,"seek-completed");'),
      'VideoPlayer_InvokeLoopPointReachedCallback_Internal':prefix('    HKVideoProbe((RuntimeObject*)___0_source,"end");'),
      'VideoPlayer_InvokeFrameReadyCallback_Internal':prefix('    HKSampleFrame(___0_source);'),
      'VideoPlayer_InvokeErrorReceivedCallback_Internal':prefix('    HKDiagnosticError((std::string("HK_VIDEO_ERROR: ") + HKUtf8(___1_errorStr)).c_str()); HKVideoProbe((RuntimeObject*)___0_source,"error");'),
    }
    edit('UnityEngine.VideoModule.cpp',changes,video_header,'#include <sstream>\n#include <unordered_map>\n')
    def ctor(body):
        needle='Exception_t* L_53 = ((Exception_t*)IL2CPP_GET_ACTIVE_EXCEPTION(Exception_t*));;'
        assert body.count(needle)==1
        body=body.replace(needle,needle+'\n\t\tHKDiagnosticError("HK_VIDEO_CTOR_EXCEPTION: original constructor caught an exception");\n\t\t'+call('Debug_LogException','L_53')+';')
        return prefix('    HKDiagnosticMessage("HK_VIDEO: constructor-enter");')(body)
    edit('TeamCherry.Cinematics.cpp',{'EmbeddedCinematicVideoPlayer__ctor':ctor},declarations(['Debug_LogException'])+'\nextern "C" void HKDiagnosticMessage(const char*);\nextern "C" void HKDiagnosticError(const char*);\n')
    # Original menu settings values when saved, without overriding them.
    quality_paths=[path for path in cpp.glob('Assembly-CSharp*.cpp') if ' GameSettings_SaveVideoSettings_' in path.read_text()]
    target=[]
    for path in quality_paths:
        if any(m[1].startswith('GameSettings_SaveVideoSettings_m') for m in METHOD.finditer(path.read_text())):target.append(path)
    assert len(target)==1
    quality='''    std::ostringstream quality;
    quality << "HK_QUALITY: saved shaderQuality=" << __this->___shaderQuality << "; particles=" << __this->___particleEffectsLevel
        << "; dithering=" << __this->___dithering << "; cameraNoise=" << __this->___cameraNoise
        << "; vsync=" << __this->___vSync << "; fps=" << __this->___targetFrameRate;
    HKDiagnosticMessage(quality.str().c_str());'''
    edit(target[0].name,{'GameSettings_SaveVideoSettings':prefix('    if ('+call('NativePreferences_get_CompatibilityLogs')+') {\n'+quality+'\n    }')},declarations(['NativePreferences_get_CompatibilityLogs'])+'extern "C" void HKDiagnosticMessage(const char*);\n','#include <sstream>\n')
    result=dict(files=report,managed_metadata_changed=False,original_game_il_changed=False,diagnostics_only=True,frame_probe_limit=60)
    (cpp.parent.parent/'diagnostics-patch.json').write_text(json.dumps(result,indent=2))
    return result

if __name__=='__main__':
    import argparse
    parser=argparse.ArgumentParser(description=__doc__);parser.add_argument('--cpp',type=Path,required=True)
    print(json.dumps(apply(parser.parse_args().cpp),indent=2))
