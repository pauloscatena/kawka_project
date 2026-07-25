namespace Skat.KawkaProject.Features.Topics;

/// <summary>
/// Which inline form is open in the topics detail panel. Modelled as one value rather than N
/// independent booleans because the forms share a single panel: two open at once puts two
/// "New count:" inputs on screen, on the one panel whose job is to make a destructive operation
/// unambiguous. One value makes the forms mutually exclusive by construction.
/// </summary>
public enum TopicsFormMode
{
    None,
    Create,
    Expand,
    Recreate
}
