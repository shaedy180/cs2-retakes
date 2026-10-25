namespace RetakesPlugin;

// A callback created before disable must never become valid again on re-enable.
internal sealed class ActivationGate
{
    internal int Generation { get; private set; }
    internal bool Enabled { get; private set; } = true;
    internal void Reset(bool enabled) { Generation++; Enabled = enabled; }
    internal bool Allows(int generation) => Enabled && generation == Generation;
}
