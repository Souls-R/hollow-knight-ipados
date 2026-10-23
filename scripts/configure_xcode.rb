gem 'xcodeproj', '=1.27.0'
require 'xcodeproj'
root = File.expand_path(ARGV.fetch(0))
bundle_id = ARGV.fetch(1)
p = Xcodeproj::Project.open(File.join(root, 'Unity-iPhone.xcodeproj'))
framework = p.targets.find { |t| t.name == 'UnityFramework' }
app = p.targets.find { |t| t.name == 'Unity-iPhone' }
game = p.targets.find { |t| t.name == 'GameAssembly' }
if game
  framework.dependencies.select { |d| d.target == game }.each(&:remove_from_project)
  game.remove_from_project
end
p.targets.select { |t| t.name.include?('Tests') }.each(&:remove_from_project)
p.files.select { |f| f.path == 'libGameAssembly.a' }.each { |f| f.path = 'Libraries/libGameAssembly.a'; f.source_tree = 'SOURCE_ROOT' }
p.build_configurations.each do |c|
  c.build_settings['PRODUCT_NAME_APP'] = 'Unity-iPhone'
  c.build_settings['IPHONEOS_DEPLOYMENT_TARGET'] = '15.0'
end
p.targets.each do |t|
  t.build_configurations.each do |c|
    b = c.build_settings
    b['CODE_SIGNING_ALLOWED'] = 'NO'
    b['CODE_SIGNING_REQUIRED'] = 'NO'
    b['IPHONEOS_DEPLOYMENT_TARGET'] = '15.0'
    b['ARCHS'] = 'arm64'
    b['ENABLE_BITCODE'] = 'NO'
    b['ENABLE_USER_SCRIPT_SANDBOXING'] = 'NO'
    b['GCC_PREPROCESSOR_DEFINITIONS'] = ['$(inherited)','UNITY_VERSION_VER=6000','UNITY_VERSION_MAJ=0','UNITY_VERSION_MIN=61','BASELIB_INLINE_NAMESPACE=il2cpp_baselib']
    b['CLANG_CXX_LANGUAGE_STANDARD'] = 'c++17'
    b['CLANG_CXX_LIBRARY'] = 'libc++'
    b['HEADER_SEARCH_PATHS'] = ['$(inherited)','$(SRCROOT)/Classes','$(SRCROOT)/Classes/Unity','$(SRCROOT)/Libraries','$(SRCROOT)/Il2CppOutput','$(SRCROOT)/../IL2CPP/libil2cpp','$(SRCROOT)/../IL2CPP/libil2cpp/pch','$(SRCROOT)/../IL2CPP/external/baselib/Include','$(SRCROOT)/../IL2CPP/external/baselib/Platforms/IOS/Include']
    b['PRODUCT_BUNDLE_IDENTIFIER'] = t == app ? bundle_id : 'com.unity3d.framework'
    b['PRODUCT_NAME'] = 'Unity-iPhone' if t == app
    b['ASSETCATALOG_COMPILER_APPICON_NAME'] = t == app && File.directory?(File.join(root,'NativeIcons.xcassets')) ? 'AppIcon' : ''
  end
end
app.product_reference.path = 'Unity-iPhone.app'
p.files.select { |f| f.path.to_s.end_with?('Images.xcassets') }.each do |f|
  f.referrers.grep(Xcodeproj::Project::Object::PBXBuildFile).each(&:remove_from_project)
  f.remove_from_project
end
data = p.files.find { |f| f.path == 'Data' } || p.main_group.new_file('Data', 'SOURCE_ROOT')
data.last_known_file_type = 'folder'
app.resources_build_phase.add_file_reference(data) unless app.resources_build_phase.files_references.include?(data)
%w[Metal MetalKit GameController CoreHaptics Security WebKit SafariServices Accelerate CoreText].each do |name|
  framework.add_system_framework(name) unless framework.frameworks_build_phase.files_references.any? { |f| f.path.to_s.end_with?(name + '.framework') }
end
%w[RegisterModules.cpp NativeKeyboard.mm NativeMouse.mm NativeControllerInput.mm NativeHaptics.mm NativeLaunch.mm].each do |name|
  path = 'Libraries/' + name
  next unless File.exist?(File.join(root,path))
  ref = p.files.find { |f| f.path == path } || p.main_group.new_file(path, 'SOURCE_ROOT')
  build = framework.source_build_phase.files.find { |f| f.file_ref == ref } || framework.source_build_phase.add_file_reference(ref)
  build.settings = {'COMPILER_FLAGS' => '-fobjc-arc'} if name.end_with?('.mm')
end
%w[NativeLaunchAssets NativeIcons.xcassets Settings.bundle zh-Hans.lproj zh-Hant.lproj en.lproj].each do |path|
  next unless File.exist?(File.join(root,path))
  ref = p.files.find { |f| f.path == path } || p.main_group.new_file(path, 'SOURCE_ROOT')
  ref.last_known_file_type = path.end_with?('.xcassets') ? 'folder.assetcatalog' : 'folder'
  app.resources_build_phase.add_file_reference(ref) unless app.resources_build_phase.files_references.include?(ref)
end
Dir.glob(File.join(root,'Libraries/*.a')).each do |path|
  name = File.basename(path)
  next if ['libiPhone-lib.a','libGameAssembly.a'].include?(name)
  ref = p.files.find { |f| f.path == 'Libraries/' + name } || p.main_group.new_file('Libraries/' + name, 'SOURCE_ROOT')
  framework.frameworks_build_phase.add_file_reference(ref) unless framework.frameworks_build_phase.files_references.include?(ref)
end
p.save
if File.exist?(File.join(root,'HollowKnightResourceContract.json'))
  %w[UIKit Foundation].each do |name|
    app.add_system_framework(name) unless app.frameworks_build_phase.files_references.any? { |f| f.path.to_s.end_with?(name + '.framework') }
  end
  path = 'HollowKnightResourceContract.json'
  ref = p.files.find { |f| f.path == path } || p.main_group.new_file(path,'SOURCE_ROOT')
  app.resources_build_phase.add_file_reference(ref) unless app.resources_build_phase.files_references.include?(ref)
  path = 'Libraries/NativeSplitResources.mm'
  ref = p.files.find { |f| f.path == path } || p.main_group.new_file(path,'SOURCE_ROOT')
  build = app.source_build_phase.files.find { |f| f.file_ref == ref } || app.source_build_phase.add_file_reference(ref)
  build.settings = {'COMPILER_FLAGS' => '-fobjc-arc'}
  main = app.source_build_phase.files.find { |f| f.file_ref.path.to_s.end_with?('main.mm') }
  main.settings = {'COMPILER_FLAGS' => '-fobjc-arc'} if main
  p.save
end
