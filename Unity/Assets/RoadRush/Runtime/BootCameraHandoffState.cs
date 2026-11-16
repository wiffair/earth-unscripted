using System;

namespace RoadRush
{
    /// <summary>
    /// Session-local main-thread camera handoff state. A boot generation is an
    /// opaque lifetime, not a frame delay. Only a render callback for the currently
    /// observed actual drive camera may retire the clearing camera.
    /// </summary>
    internal sealed class BootCameraHandoffState
    {
        internal int Generation { get; private set; }
        internal bool Waiting { get; private set; }
        internal bool DriveObserved { get; private set; }
        internal bool RenderConfirmed { get; private set; }
        internal bool Disposed { get; private set; }

        internal void Begin(int generation)
        {
            if (Disposed) throw new ObjectDisposedException(nameof(BootCameraHandoffState));
            if (generation < Generation)
                throw new ArgumentOutOfRangeException(nameof(generation), "Stale boot generation");
            Generation = generation;
            Waiting = true;
            DriveObserved = false;
            RenderConfirmed = false;
        }

        internal bool ObserveCurrentDrive(int generation)
        {
            if (Disposed || !Waiting || generation != Generation) return false;
            DriveObserved = true;
            return true;
        }

        internal bool ConfirmCurrentRender(int generation)
        {
            if (Disposed || !Waiting || !DriveObserved || generation != Generation) return false;
            Waiting = false;
            RenderConfirmed = true;
            return true;
        }

        internal void Dispose()
        {
            Disposed = true;
            Waiting = false;
            DriveObserved = false;
        }
    }
}
