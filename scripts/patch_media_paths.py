"""Use canonical raw file paths at the cinematic VideoPlayer URL boundary.

The desktop constructor percent-encodes a symlink path as file://. iOS reports
Cannot read file before preparation. Resolve the already File.Exists-checked
local path and pass the raw absolute filename supported by VideoPlayer.url.
No asset copy, automatic skipping, or managed metadata change.
"""
from pathlib import Path
import re

MARKER = '// HK_MEDIA_PATH_022'
METHOD = re.compile(r'^IL2CPP_EXTERN_C IL2CPP_METHOD_ATTR [^\n]+? (\w+) \([^\n]+\)\s*\n\{', re.M)

def apply(cpp):
    path = cpp/'TeamCherry.Cinematics.cpp'
    source = path.read_text().lstrip('\ufeff')
    assert MARKER not in source
    setter = re.compile(r'(VideoPlayer_set_url_\w+\(L_42, )L_45(, NULL\);)')
    assert len(setter.findall(source)) == 1
    source = setter.sub(r'\1HKMoviePath(L_43)\2', source)
    header = r'''
// HK_MEDIA_PATH_022
extern "C" void HKDiagnosticMessage(const char*);
extern "C" void HKDiagnosticError(const char*);
static String_t* HKMoviePath(String_t* original)
{
    if (!original) return original;
    char* path = il2cpp_codegen_marshal_string(original);
    if (!path) return original;
    char* resolved = realpath(path, nullptr);
    const char* target = resolved ? resolved : path;
    FILE* file = fopen(target, "rb");
    std::string message = std::string("HK_VIDEO_PATH: readable=") + (file ? "1" : "0")
        + "; resolved=" + (resolved ? "1" : "0") + "; path=" + target;
    if (file) { fclose(file); HKDiagnosticMessage(message.c_str()); }
    else HKDiagnosticError(message.c_str());
    String_t* result = il2cpp_codegen_string_new_wrapper(target);
    free(resolved);
    il2cpp_codegen_marshal_free(path);
    return result;
}
'''
    pos = METHOD.search(source).start()
    source = '#include <cstdio>\n#include <cstdlib>\n#include <string>\n' + source[:pos]+header+source[pos:]
    path.write_text(source)
    # Observe quality values at load and every original menu application. The
    # original menu/control/serialization code still performs all changes.
    for path in cpp.glob('Assembly-CSharp*.cpp'):
        source = path.read_text().lstrip('\ufeff')
        edits = []
        for match in METHOD.finditer(source):
            name = match[1].rsplit('_m',1)[0]
            if name not in ('GameSettings_LoadVideoSettings', 'MenuSetting_UpdateSetting'): continue
            end = source.index('\n}', match.end())+2
            body = source[match.start():end]
            obj = '__this' if name.startswith('GameSettings_') else '__this->___gs'
            prefix = 'load' if name.startswith('GameSettings_') else 'apply'
            extra = '' if prefix == 'load' else ' << "; option=" << __this->___settingType << "; index=" << ___0_settingIndex'
            probe = '''{ auto settings = OBJ; if(settings) {
                std::ostringstream values; values << "HK_QUALITY: STAGE" EXTRA
                    << "; shaderQuality=" << settings->___shaderQuality << "; particles=" << settings->___particleEffectsLevel
                    << "; dithering=" << settings->___dithering << "; cameraNoise=" << settings->___cameraNoise
                    << "; vsync=" << settings->___vSync << "; fps=" << settings->___targetFrameRate;
                HKDiagnosticMessage(values.str().c_str());
            } }'''.replace('OBJ',obj).replace('STAGE',prefix).replace('EXTRA',extra)
            # Gate string construction itself so default runs have no work.
            flags = 'NativePreferences_get_CompatibilityLogs_m06ED16B9D8D4D16264A6ADAAF561D98A4D562A07'
            probe = 'if ('+flags+'(NULL)) '+probe
            assert 'return;' in body
            body = body.replace('return;', probe+'\n\t\treturn;')
            edits.append((match.start(), end, body))
        if edits:
            for start,end,body in reversed(edits): source=source[:start]+body+source[end:]
            if 'extern "C" void HKDiagnosticMessage' not in source:
                pos = METHOD.search(source).start()
                source=source[:pos]+'extern "C" void HKDiagnosticMessage(const char*);\nIL2CPP_EXTERN_C IL2CPP_METHOD_ATTR bool NativePreferences_get_CompatibilityLogs_m06ED16B9D8D4D16264A6ADAAF561D98A4D562A07(const RuntimeMethod*);\n'+source[pos:]
            path.write_text('#include <sstream>\n'+source)
    return dict(video_path='canonical raw local path',quality_probes=['load','apply'],metadata_changed=False)

if __name__ == '__main__':
    import argparse
    parser=argparse.ArgumentParser();parser.add_argument('--cpp',type=Path,required=True)
    print(apply(parser.parse_args().cpp))
