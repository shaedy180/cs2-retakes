using RetakesPlugin;

var gate = new ActivationGate();
var first = gate.Generation;
if (!gate.Allows(first)) throw new Exception("Initial callback rejected");
gate.Reset(false);
if (gate.Allows(first) || gate.Allows(gate.Generation)) throw new Exception("Disabled callback executed");
gate.Reset(true);
if (gate.Allows(first) || !gate.Allows(gate.Generation)) throw new Exception("Old callback resurrected on re-enable");
for (int i = 0; i < 10000; i++)
{
    int lease = gate.Generation;
    gate.Reset(false);
    gate.Reset(true);
    if (gate.Allows(lease)) throw new Exception("Stale player/round callback resurrected");
}
Console.WriteLine("PASS initial enable, immediate disable, reinitialization and 10000 stale callback generations");
