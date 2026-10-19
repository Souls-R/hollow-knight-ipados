#import "NativeSplitPaths.h"

static BOOL Fail(NSString** reason, NSString* message)
{
    if (reason) *reason = message;
    return NO;
}

static NSDictionary* ReadJSON(NSString* path)
{
    NSData* data = [NSData dataWithContentsOfFile:path];
    if (!data) return nil;
    id value = [NSJSONSerialization JSONObjectWithData:data options:0 error:NULL];
    return [value isKindOfClass:NSDictionary.class] ? value : nil;
}

static BOOL SafeFilePath(NSString* path)
{
    if (![path isKindOfClass:NSString.class] || ![path hasPrefix:@"Data/"] ||
        [path containsString:@"\\"] || [path rangeOfCharacterFromSet:NSCharacterSet.controlCharacterSet].location != NSNotFound) return NO;
    for (NSString* part in path.pathComponents)
        if ([part isEqualToString:@".."] || [part isEqualToString:@"."] || [part isEqualToString:@"/"]) return NO;
    return [path isEqualToString:path.stringByStandardizingPath] && path.pathComponents.count > 1;
}

static BOOL RegularFile(NSString* root, NSString* relative, unsigned long long bytes)
{
    NSFileManager* fm = NSFileManager.defaultManager;
    NSString* path = root;
    NSArray* parts = relative.pathComponents;
    for (NSUInteger i = 0; i < parts.count; ++i)
    {
        path = [path stringByAppendingPathComponent:parts[i]];
        NSDictionary* attrs = [fm attributesOfItemAtPath:path error:NULL];
        NSString* type = attrs[NSFileType];
        if (i + 1 < parts.count)
        {
            if (![type isEqualToString:NSFileTypeDirectory]) return NO;
        }
        else if (![type isEqualToString:NSFileTypeRegular] || [attrs[NSFileSize] unsignedLongLongValue] != bytes) return NO;
    }
    return YES;
}

BOOL HKPrepareResourceOverlay(NSString* app, NSString* documents, NSString* runtime, NSString** reason)
{
    NSFileManager* fm = NSFileManager.defaultManager;
    if (![runtime.lastPathComponent isEqualToString:@"HollowKnightRuntime-v1"] || !runtime.isAbsolutePath)
        return Fail(reason, @"启动目录无效。");
    NSDictionary* contract = ReadJSON([app stringByAppendingPathComponent:@"HollowKnightResourceContract.json"]);
    if (![contract[@"schema"] isEqual:@1] || ![contract[@"folder"] isEqual:@"HollowKnightResources-v1"] ||
        ![contract[@"files"] isKindOfClass:NSArray.class] || ![contract[@"compatibility"] isKindOfClass:NSString.class])
        return Fail(reason, @"应用资源配置无效，请重新安装程序包。");
    NSString* pack = [documents stringByAppendingPathComponent:contract[@"folder"]];
    NSDictionary* packAttrs = [fm attributesOfItemAtPath:pack error:NULL];
    if (![packAttrs[NSFileType] isEqual:NSFileTypeDirectory])
        return Fail(reason, @"尚未找到资源。请把 HollowKnightResources-v1 文件夹放入本应用的文件夹。");
    NSDictionary* info = ReadJSON([pack stringByAppendingPathComponent:@"pack.json"]);
    if (![info[@"schema"] isEqual:@1] || ![info[@"pack_id"] isEqual:contract[@"pack_id"]] ||
        ![info[@"compatibility"] isEqual:contract[@"compatibility"]])
        return Fail(reason, @"资源版本不匹配，请导入与程序配套的资源包。");
    NSDictionary* index = ReadJSON([pack stringByAppendingPathComponent:@"resource-index.json"]);
    if (![index[@"files"] isEqual:contract[@"files"]])
        return Fail(reason, @"资源清单不匹配，请重新导入资源包。");
    NSMutableDictionary<NSString*,NSString*>* routes = [NSMutableDictionary dictionary];
    for (NSDictionary* record in contract[@"files"])
    {
        if (![record isKindOfClass:NSDictionary.class]) return Fail(reason, @"资源清单格式无效。");
        NSString* relative = record[@"path"];
        NSNumber* size = record[@"bytes"];
        if (!SafeFilePath(relative) || ![size isKindOfClass:NSNumber.class] || size.longLongValue < 0 || routes[relative])
            return Fail(reason, @"资源清单包含无效路径。");
        if (!RegularFile(pack, relative, size.unsignedLongLongValue))
            return Fail(reason, [@"资源不完整：" stringByAppendingString:relative.lastPathComponent]);
        routes[relative] = [pack stringByAppendingPathComponent:relative];
    }
    NSString* core = [app stringByAppendingPathComponent:@"Data"];
    NSDirectoryEnumerator* enumerator = [fm enumeratorAtPath:core];
    if (!enumerator) return Fail(reason, @"程序启动文件缺失，请重新安装程序包。");
    for (NSString* relative in enumerator)
    {
        NSString* source = [core stringByAppendingPathComponent:relative];
        NSDictionary* attrs = [fm attributesOfItemAtPath:source error:NULL];
        if ([attrs[NSFileType] isEqual:NSFileTypeDirectory]) continue;
        NSString* target = [@"Data" stringByAppendingPathComponent:relative];
        if (![attrs[NSFileType] isEqual:NSFileTypeRegular] || !SafeFilePath(target) || routes[target])
            return Fail(reason, @"程序启动文件与资源发生冲突。");
        routes[target] = source;
    }
    if (!routes[@"Data/Managed/Metadata/global-metadata.dat"] || !routes[@"Data/globalgamemanagers"] || !routes[@"Data/level0"])
        return Fail(reason, @"启动必需文件缺失。");
    // Every link is recreated before initializing Unity. This refreshes absolute
    // bundle paths after a signed app update while retaining imported assets.
    NSString* temporary = [runtime stringByAppendingString:@".building"];
    [fm removeItemAtPath:temporary error:NULL];
    NSError* error = nil;
    if (![fm createDirectoryAtPath:temporary withIntermediateDirectories:YES attributes:nil error:&error])
        return Fail(reason, @"无法创建启动目录。");
    for (NSString* relative in routes)
    {
        NSString* destination = [temporary stringByAppendingPathComponent:relative];
        if (![fm createDirectoryAtPath:destination.stringByDeletingLastPathComponent withIntermediateDirectories:YES attributes:nil error:&error] ||
            ![fm createSymbolicLinkAtPath:destination withDestinationPath:routes[relative] error:&error])
            return Fail(reason, @"无法建立资源加载路径。");
    }
    [fm removeItemAtPath:runtime error:NULL];
    if (![fm moveItemAtPath:temporary toPath:runtime error:&error]) return Fail(reason, @"无法准备资源加载目录。");
    [[NSURL fileURLWithPath:runtime] setResourceValue:@YES forKey:NSURLIsExcludedFromBackupKey error:NULL];
    [[NSURL fileURLWithPath:pack] setResourceValue:@YES forKey:NSURLIsExcludedFromBackupKey error:NULL];
    if (reason) *reason = nil;
    return YES;
}
