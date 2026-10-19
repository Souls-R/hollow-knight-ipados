"""Extract first-game artwork from the user's original macOS app."""
import json
import plistlib
from pathlib import Path

ROOT = Path(__file__).resolve().parents[1]

def write(path, value):
    path.parent.mkdir(parents=True, exist_ok=True)
    path.write_text(json.dumps(value, indent=2, ensure_ascii=False), encoding='utf-8')

def strings_file(values):
    return '\n'.join(json.dumps(k,ensure_ascii=False)+' = '+json.dumps(v,ensure_ascii=False)+';' for k,v in values.items())+'\n'

def configure(project, game):
    import UnityPy
    from PIL import Image
    output=project/'NativeLaunchAssets'
    output.mkdir(exist_ok=True)
    requests={
        'resources.assets': {'Warning_Fleur0008': 'Warning_Fleur0008.png', 'Perpetua': 'Perpetua.otf'},
        'sharedassets0.assets': {'main_menu_pointer': 'main_menu_cursor_small0010.png', 'NotoSerifCJKsc-Regular': 'NotoSerifCJKsc-Regular.otf'},
        'sharedassets1.assets': {'Voidheart_menu_BG': 'Voidheart_menu_BG.png'},
    }
    receipt=[]
    for file, names in requests.items():
        found=set()
        env=UnityPy.load(str(game/file))
        for obj in env.objects:
            if obj.type.name not in ('Sprite','Font'): continue
            asset=obj.read()
            if asset.m_Name not in names: continue
            name=asset.m_Name
            assert name not in found
            if obj.type.name=='Font': (output/names[name]).write_bytes(bytes(asset.m_FontData))
            else: asset.image.save(output/names[name])
            found.add(name)
            receipt.append(dict(source=file, id=obj.path_id, name=name, output=names[name]))
        assert found==set(names), (file,found)
    strings=json.loads((ROOT/'src/launch/strings.json').read_text(encoding='utf-8'))
    write(output/'strings.json',strings)
    with Image.open(game.parent/'PlayerIcon.icns') as image:
        image.size=(1024,1024)
        rgba=image.convert('RGBA')
    icon=Image.alpha_composite(Image.new('RGBA',rgba.size,(0,0,0,255)),rgba).convert('RGB')
    catalog=project/'NativeIcons.xcassets'
    icons=catalog/'AppIcon.appiconset'
    icons.mkdir(parents=True,exist_ok=True)
    write(catalog/'Contents.json',dict(info=dict(version=1,author='xcode')))
    images=[]
    for idiom,sizes in [('iphone',[(20,2),(20,3),(29,2),(29,3),(40,2),(40,3),(60,2),(60,3)]),('ipad',[(20,1),(20,2),(29,1),(29,2),(40,1),(40,2),(76,1),(76,2),(83.5,2)]),('ios-marketing',[(1024,1)])]:
        for size,scale in sizes:
            name=f'{idiom}-{size}-{scale}.png'
            icon.resize((int(size*scale),)*2,Image.Resampling.LANCZOS).save(icons/name)
            images.append(dict(idiom=idiom,size=f'{size}x{size}',scale=f'{scale}x',filename=name))
    write(icons/'Contents.json',dict(images=images,info=dict(version=1,author='xcode')))
    write(catalog/'NativeLaunchBackground.colorset/Contents.json',dict(info=dict(version=1,author='xcode'),colors=[dict(idiom='universal',color={'color-space':'srgb','components':dict(red='0.000',green='0.000',blue='0.000',alpha='1.000')})]))
    settings=project/'Settings.bundle'
    settings.mkdir(exist_ok=True)
    specifiers=[dict(Type='PSGroupSpecifier',Title='sectionLaunch',FooterText='settingsFoot')]
    for key,title,default in [('NativeShowNotice','settingNotice',True),('NativeDiagnosticLogs','settingLogs',False),('NativeMetalHUD','settingHud',False),('NativeStartupDetails','settingDetails',False)]:
        if key=='NativeDiagnosticLogs':specifiers.append(dict(Type='PSGroupSpecifier',Title='sectionDiagnostics'))
        specifiers.append(dict(Type='PSToggleSwitchSpecifier',Key=key,Title=title,DefaultValue=default))
    (settings/'Root.plist').write_bytes(plistlib.dumps(dict(PreferenceSpecifiers=specifiers,StringsTable='Root')))
    (settings/'Info.plist').write_bytes(plistlib.dumps(dict(CFBundleIdentifier='native.settings',CFBundleVersion='1',CFBundlePackageType='BNDL')))
    for language,values in strings.items():
        lproj=project/(language+'.lproj');lproj.mkdir(exist_ok=True)
        (lproj/'InfoPlist.strings').write_text(strings_file(dict(CFBundleDisplayName=values['appName'],CFBundleName=values['appName'])),encoding='utf-8')
        localized=settings/(language+'.lproj');localized.mkdir(exist_ok=True)
        (localized/'Root.strings').write_text(strings_file({k:v for k,v in values.items() if k.startswith(('setting','section'))}),encoding='utf-8')
    info_path=project/'Info.plist';info=plistlib.loads(info_path.read_bytes())
    for key in ('UILaunchStoryboardName','UILaunchStoryboardName~ipad','UILaunchScreens','MetalHudEnabled'):info.pop(key,None)
    info.update(UILaunchScreen={'UIColorName':'NativeLaunchBackground'},CFBundleDisplayName='Hollow Knight',CFBundleName='Hollow Knight',CFBundleDevelopmentRegion='en',CFBundleLocalizations=list(strings))
    info_path.write_bytes(plistlib.dumps(info))
    write(project.parent/'launch-artwork-receipt.json',receipt)
