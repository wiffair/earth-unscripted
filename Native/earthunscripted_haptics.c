#include <SDL3/SDL.h>
#include <string.h>
#include <ctype.h>
#include <math.h>

#if defined(_WIN32)
#define EU_EXPORT __declspec(dllexport)
#else
#define EU_EXPORT __attribute__((visibility("default")))
#endif

static SDL_Joystick *wheel;
static SDL_Haptic *haptic;
static SDL_HapticEffect effect;
static int effect_id = -1;
static bool initialized;
static char last_error[512] = "No wheel opened";

static void record_error(void) {
    SDL_strlcpy(last_error, SDL_GetError(), sizeof(last_error));
}
static bool contains_case_insensitive(const char *text, const char *part) {
    if (!part || !*part) return false; /* Never select an arbitrary haptic device. */
    if (!text) return false;
    for (; *text; ++text) {
        const char *a=text,*b=part;
        while (*a && *b && tolower((unsigned char)*a)==tolower((unsigned char)*b)) { ++a; ++b; }
        if (!*b) return true;
    }
    return false;
}
EU_EXPORT void eu_stop(void) {
    if (haptic) SDL_StopHapticEffects(haptic);
}
EU_EXPORT void eu_close(void) {
    eu_stop();
    if (haptic) {
        if (effect_id >= 0) SDL_DestroyHapticEffect(haptic, effect_id);
        SDL_CloseHaptic(haptic);
    }
    haptic=NULL; effect_id=-1;
    if (wheel) SDL_CloseJoystick(wheel);
    wheel=NULL;
    if (initialized) SDL_QuitSubSystem(SDL_INIT_JOYSTICK | SDL_INIT_HAPTIC);
    initialized=false;
}
EU_EXPORT const char *eu_error(void) { return last_error; }
EU_EXPORT int eu_open(const char *device_name) {
    eu_close();
    SDL_SetHint(SDL_HINT_JOYSTICK_ALLOW_BACKGROUND_EVENTS,"0");
    if (!SDL_InitSubSystem(SDL_INIT_JOYSTICK | SDL_INIT_HAPTIC)) { record_error(); return 0; }
    initialized=true;
    int count=0;
    SDL_JoystickID *devices=SDL_GetJoysticks(&count);
    for(int i=0;devices && i<count;i++) {
        if(!contains_case_insensitive(SDL_GetJoystickNameForID(devices[i]),device_name))continue;
        wheel=SDL_OpenJoystick(devices[i]);
        if(!wheel)continue;
        if(SDL_IsJoystickHaptic(wheel))haptic=SDL_OpenHapticFromJoystick(wheel);
        if(haptic)break;
        SDL_CloseJoystick(wheel);wheel=NULL;
    }
    SDL_free(devices);
    if(!haptic){SDL_strlcpy(last_error,"No matching force-feedback wheel. Check G29 USB, power and G HUB.",sizeof(last_error));eu_close();return 0;}
    if(!(SDL_GetHapticFeatures(haptic)&SDL_HAPTIC_CONSTANT)){SDL_strlcpy(last_error,"Wheel driver has no constant-force support",sizeof(last_error));eu_close();return 0;}
    SDL_SetHapticGain(haptic,65);
    SDL_SetHapticAutocenter(haptic,0);
    SDL_zero(effect);
    effect.type=SDL_HAPTIC_CONSTANT;
    effect.constant.direction.type=SDL_HAPTIC_STEERING_AXIS;
    effect.constant.length=120; /* Hardware watchdog: force expires if the game stalls. */
    effect.constant.attack_length=8;
    effect.constant.fade_length=15;
    effect.constant.level=0;
    effect_id=SDL_CreateHapticEffect(haptic,&effect);
    if(effect_id<0){record_error();eu_close();return 0;}
    SDL_strlcpy(last_error,"Ready",sizeof(last_error));return 1;
}
EU_EXPORT int eu_force(float amount) {
    if(!haptic||!wheel||effect_id<0)return 0;
    SDL_UpdateJoysticks();
    if(!SDL_JoystickConnected(wheel)){eu_stop();return 0;}
    if(!isfinite(amount))amount=0;
    if(amount>.65f)amount=.65f;
    if(amount<-.65f)amount=-.65f;
    effect.constant.level=(Sint16)(amount*32767);
    if(!SDL_UpdateHapticEffect(haptic,effect_id,&effect)||!SDL_RunHapticEffect(haptic,effect_id,1)){record_error();eu_stop();return 0;}
    return 1;
}
