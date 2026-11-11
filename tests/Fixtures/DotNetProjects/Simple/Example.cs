namespace VerificationFixture;

public class Example
{
    // Intentional semantic error, used by the read-only analyzer fixture.
    public int BrokenValue => "not an integer";

    public void IgnoredException()
    {
        try { throw new System.InvalidOperationException("fixture"); }
        catch { }
    }
}
