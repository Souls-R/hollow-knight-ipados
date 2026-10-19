#import <Foundation/Foundation.h>

static inline NSString* HKRuntimeRoot(void)
{
    NSString* library = NSSearchPathForDirectoriesInDomains(NSApplicationSupportDirectory, NSUserDomainMask, YES).firstObject;
    return [library stringByAppendingPathComponent:@"HollowKnightRuntime-v1"];
}

BOOL HKPrepareResourceOverlay(NSString* app, NSString* documents, NSString* runtime, NSString** reason);
