using System;
namespace EarthUnscripted.Editor
{
    // Throws immediately inside each existing proof's try/catch, preserving its
    // own failure reporting/cleanup instead of waiting for a generic timeout.
    public static class EditorBootReadiness
    {
        public static bool Ready(GameSession session)
        {
            if(!session)return false;
            if(session.BootFailed)throw new InvalidOperationException("GameSession boot failed: "+session.BootStatus);
            return session.BootReady;
        }
    }
}
