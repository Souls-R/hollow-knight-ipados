#import <UnityFramework/UnityFramework.h>
#import "NativeSplitPaths.h"
#include <dlfcn.h>
#include <stdio.h>

// Keep the existing dynamic framework loading boundary: Settings defaults and
// Metal HUD environment must be ready before the framework is loaded.
static void* NativeLaunchSymbol(const char* name)
{
    static void* library;
    if (!library) {
        NSString* path = [NSBundle.mainBundle.bundlePath stringByAppendingPathComponent:@"Frameworks/UnityFramework.framework/UnityFramework"];
        library = dlopen(path.fileSystemRepresentation, RTLD_LAZY | RTLD_LOCAL);
    }
    return library ? dlsym(library, name) : nullptr;
}
static void NativeLaunch_SetHost(UIView* host)
{
    auto call = (void (*)(UIView*))NativeLaunchSymbol("NativeLaunch_SetHost");
    if (call) call(host);
    else {
        UILabel* error = [[UILabel alloc] initWithFrame:host.bounds];
        error.text = @"无法载入游戏程序，请重新安装 IPA。";
        error.textColor = UIColor.whiteColor; error.textAlignment = NSTextAlignmentCenter;
        error.autoresizingMask = UIViewAutoresizingFlexibleWidth | UIViewAutoresizingFlexibleHeight;
        [host addSubview:error];
    }
}
static void NativeLaunch_Show(int page, const char* detail)
{ auto call = (void (*)(int,const char*))NativeLaunchSymbol("NativeLaunch_Show"); if (call) call(page,detail); }
static int NativeLaunch_Poll()
{ auto call = (int (*)())NativeLaunchSymbol("NativeLaunch_Poll"); return call ? call() : 0; }
static void NativeLaunch_Hide()
{ auto call = (void (*)())NativeLaunchSymbol("NativeLaunch_Hide"); if (call) call(); }

static int launchArgc;
static char** launchArgv;

static NSString* HKLaunchLogPath()
{
    return [NSSearchPathForDirectoriesInDomains(NSDocumentDirectory,NSUserDomainMask,YES).firstObject stringByAppendingPathComponent:@"native-launch.log"];
}
static void HKLaunchLog(NSString* message, BOOL error)
{
    if (!error && ![NSUserDefaults.standardUserDefaults boolForKey:@"NativeDiagnosticLogs"]) return;
    NSData* line = [[NSString stringWithFormat:@"%@ %@%@\n", NSDate.date, error ? @"ERROR: " : @"", message] dataUsingEncoding:NSUTF8StringEncoding];
    FILE* file = fopen(HKLaunchLogPath().fileSystemRepresentation, "ab");
    if (!file) return;
    if (fseek(file, 0, SEEK_END) == 0) {
        long size = ftell(file);
        if (size >= 0 && size < 256 * 1024) fwrite(line.bytes, 1, line.length, file);
    }
    fclose(file);
}

static UnityFramework* LoadUnity()
{
    NSString* path = [NSBundle.mainBundle.bundlePath stringByAppendingPathComponent:@"Frameworks/UnityFramework.framework"];
    NSBundle* bundle = [NSBundle bundleWithPath:path];
    if (!bundle.isLoaded) [bundle load];
    UnityFramework* unity = [bundle.principalClass getInstance];
    if (!unity.appController) [unity setExecuteHeader:&_mh_execute_header];
    return unity;
}

@interface HKResourceController : UIViewController
@end
@implementation HKResourceController
- (BOOL)prefersStatusBarHidden { return YES; }
- (UIInterfaceOrientationMask)supportedInterfaceOrientations { return UIInterfaceOrientationMaskLandscape; }
@end

@interface HKResourceDelegate : UIResponder<UIApplicationDelegate>
@property(nonatomic,strong) UIWindow* window;
@property(nonatomic,strong) NSTimer* launchTimer;
@property(nonatomic,assign) BOOL noticeAccepted;
@property(nonatomic,strong) UnityFramework* unity;
@property(nonatomic,strong) NSDictionary* options;
@property(nonatomic,assign) BOOL checking;
@property(nonatomic,assign) BOOL started;
@end

@implementation HKResourceDelegate
- (BOOL)application:(UIApplication*)application didFinishLaunchingWithOptions:(NSDictionary*)options
{
    self.options = options;
    [@"Hollow Knight native launcher\n" writeToFile:HKLaunchLogPath() atomically:YES encoding:NSUTF8StringEncoding error:nil];
    HKLaunchLog([NSString stringWithFormat:@"app=%@ build=%@", [NSBundle.mainBundle objectForInfoDictionaryKey:@"CFBundleShortVersionString"], [NSBundle.mainBundle objectForInfoDictionaryKey:@"CFBundleVersion"]], NO);
    self.window = [[UIWindow alloc] initWithFrame:UIScreen.mainScreen.bounds];
    HKResourceController* controller = [HKResourceController new];
    self.window.rootViewController = controller;
    controller.view.backgroundColor = [UIColor colorWithWhite:0.055 alpha:1];
    NativeLaunch_SetHost(controller.view);
    self.noticeAccepted = ![NSUserDefaults.standardUserDefaults boolForKey:@"NativeShowNotice"];
    self.launchTimer = [NSTimer scheduledTimerWithTimeInterval:.1 target:self selector:@selector(pollLaunch:) userInfo:nil repeats:YES];
    [self.window makeKeyAndVisible];
    if (!self.noticeAccepted) NativeLaunch_Show(1, "");
    return YES;
}
- (void)pollLaunch:(NSTimer*)timer
{
    int action = NativeLaunch_Poll();
    if (action == 1) { self.noticeAccepted = YES; [self checkResources]; }
    else if (action == 2) [self checkResources];
}
- (void)checkResources
{
    if (self.started || self.checking || !self.noticeAccepted) return;
    self.checking = YES; NativeLaunch_Show(3, "");
    HKLaunchLog(@"Checking resource pack and rebuilding program/data routes", NO);
    NSString* app = NSBundle.mainBundle.bundlePath;
    NSString* documents = NSSearchPathForDirectoriesInDomains(NSDocumentDirectory,NSUserDomainMask,YES).firstObject;
    dispatch_async(dispatch_get_global_queue(QOS_CLASS_USER_INITIATED,0), ^{
        NSString* message = nil;
        BOOL ready = HKPrepareResourceOverlay(app,documents,HKRuntimeRoot(),&message);
        dispatch_async(dispatch_get_main_queue(), ^{
            self.checking = NO;
            if (!ready) { HKLaunchLog(message, YES); NativeLaunch_Show(2, message.UTF8String); return; }
            if (UIApplication.sharedApplication.applicationState != UIApplicationStateActive)
            { return; }
            self.unity = LoadUnity();
            if (!self.unity) { HKLaunchLog(@"UnityFramework could not be loaded", YES); NativeLaunch_Show(6, "UnityFramework could not be loaded"); return; }
            HKLaunchLog(@"Resource pack ready; starting Unity", NO);
            [self.launchTimer invalidate]; self.launchTimer = nil;
            NativeLaunch_Hide();
            self.started = YES;
            [self.unity runEmbeddedWithArgc:launchArgc argv:launchArgv appLaunchOpts:self.options];
            UIWindow* launcher = self.window;
            self.window = self.unity.appController.window;
            launcher.hidden = YES;
        });
    });
}
- (void)applicationDidBecomeActive:(UIApplication*)app
{ if (self.started) [self.unity.appController applicationDidBecomeActive:app]; else [self checkResources]; }
- (void)applicationWillResignActive:(UIApplication*)app
{ if (self.started) [self.unity.appController applicationWillResignActive:app]; }
- (void)applicationDidEnterBackground:(UIApplication*)app
{ if (self.started) [self.unity.appController applicationDidEnterBackground:app]; }
- (void)applicationWillEnterForeground:(UIApplication*)app
{ if (self.started) [self.unity.appController applicationWillEnterForeground:app]; }
- (void)applicationWillTerminate:(UIApplication*)app
{ if (self.started) [self.unity.appController applicationWillTerminate:app]; }
- (void)applicationDidReceiveMemoryWarning:(UIApplication*)app
{ if (self.started) [self.unity.appController applicationDidReceiveMemoryWarning:app]; }
- (BOOL)application:(UIApplication*)app openURL:(NSURL*)url options:(NSDictionary<UIApplicationOpenURLOptionsKey,id>*)options
{ return self.started ? [self.unity.appController application:app openURL:url options:options] : NO; }
- (UIInterfaceOrientationMask)application:(UIApplication*)app supportedInterfaceOrientationsForWindow:(UIWindow*)window
{ return UIInterfaceOrientationMaskLandscape; }
@end

int main(int argc,char* argv[])
{
    launchArgc = argc; launchArgv = argv;
    @autoreleasepool {
        NSUserDefaults* prefs = NSUserDefaults.standardUserDefaults;
        NSString* root = [NSBundle.mainBundle pathForResource:@"Root" ofType:@"plist" inDirectory:@"Settings.bundle"];
        NSDictionary* definition = [NSDictionary dictionaryWithContentsOfFile:root];
        NSMutableDictionary* defaults = [NSMutableDictionary dictionary];
        for (NSDictionary* item in definition[@"PreferenceSpecifiers"])
            if (item[@"Key"] && item[@"DefaultValue"]) defaults[item[@"Key"]] = item[@"DefaultValue"];
        [prefs registerDefaults:defaults];
        setenv("MTL_HUD_ENABLED", [prefs boolForKey:@"NativeMetalHUD"] ? "1" : "0", 1);
        setenv("MTL_HUD_ALIGNMENT", "bottomright", 1);
        setenv("MTL_HUD_SCALE", "0.20", 1);
        setenv("MTL_HUD_ELEMENTS", "fps,frameinterval,gputime,frameintervalgraph", 1);
        setenv("MTL_HUD_LOGGING_ENABLED", "0", 1);
        return UIApplicationMain(argc,argv,nil,NSStringFromClass(HKResourceDelegate.class));
    }
}
