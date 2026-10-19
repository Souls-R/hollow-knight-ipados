#import <GameController/GameController.h>
#import <UIKit/UIKit.h>
#include <cstring>

extern "C" void HKDiagnosticMessage(const char* message);
static void ProbeControllerButtons()
{
    static int enabled = -1;
    if (enabled < 0) enabled = [NSUserDefaults.standardUserDefaults boolForKey:@"NativeDiagnosticLogs"] ? 1 : 0;
    static __weak GCController* previous;
    static unsigned buttons;
    GCController* controller = GCController.current ?: GCController.controllers.firstObject;
    GCExtendedGamepad* pad = controller.extendedGamepad;
    if (controller != previous)
    {
        previous = controller; buttons = 0;
        if (enabled)
        {
            NSString* capabilities = [NSString stringWithFormat:@"HK_GC_PROFILE: product=%@; menuPresent=%d; viewPresent=%d; viewSystemGesture=%d; viewGesturePreference=%ld; viewAliases=%@",
                controller.vendorName, pad.buttonMenu != nil, pad.buttonOptions != nil,
                pad.buttonOptions.isBoundToSystemGesture, (long)pad.buttonOptions.preferredSystemGestureState,
                pad.buttonOptions.aliases];
            HKDiagnosticMessage(capabilities.UTF8String);
        }
    }
    if (!enabled) return;
    unsigned current = (pad.buttonMenu.isPressed ? 1u : 0u) | (pad.buttonOptions.isPressed ? 2u : 0u);
    if (current == buttons) return;
    buttons = current;
    NSString* line = [NSString stringWithFormat:@"HK_GC_BUTTON: product=%@; menu=%d; view=%d", controller.vendorName, !!(current & 1), !!(current & 2)];
    HKDiagnosticMessage(line.UTF8String);
}

// Called once per InControl update; copy every physical key independently.
// There is no text-input/repeat event or single-current-key state here.
extern "C" int NativeKeyboard_CopyState(unsigned char* destination, int length)
{
    if (!destination || length < 256) return 0;
    std::memset(destination, 0, 256);
    if (UIApplication.sharedApplication.applicationState != UIApplicationStateActive) return 0;
    ProbeControllerButtons();
    GCKeyboardInput* input = [GCKeyboard coalescedKeyboard].keyboardInput;
    if (!input) return 0;
    for (int code = 4; code < 232; ++code)
        destination[code] = [input buttonForKeyCode:static_cast<GCKeyCode>(code)].pressed ? 1 : 0;
    return 1;
}
