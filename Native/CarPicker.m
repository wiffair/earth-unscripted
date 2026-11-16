#import <Cocoa/Cocoa.h>
#include <stdlib.h>
#include <string.h>

// Called synchronously on Unity's main thread from its paused import menu.
// NSOpenPanel owns its modal run loop; the returned UTF-8 path belongs to the
// caller until EarthUnscriptedFreeCarPickerPath. Cancellation returns NULL.
__attribute__((visibility("default")))
char *EarthUnscriptedChooseCarPack(void)
{
    @autoreleasepool {
        if (![NSThread isMainThread]) return NULL;
        NSOpenPanel *panel = [NSOpenPanel openPanel];
        panel.title = @"Import Earth Unscripted car pack";
        panel.message = @"Choose a .eucar vehicle pack. Your current car stays selected until you choose another.";
        panel.prompt = @"Import";
        panel.canChooseFiles = YES;
        panel.canChooseDirectories = NO;
        panel.allowsMultipleSelection = NO;
        panel.resolvesAliases = YES;
        [NSApp activateIgnoringOtherApps:YES];
        if ([panel runModal] != NSModalResponseOK || panel.URL == nil) return NULL;
        return strdup(panel.URL.fileSystemRepresentation);
    }
}

__attribute__((visibility("default")))
void EarthUnscriptedFreeCarPickerPath(char *path)
{
    free(path);
}
