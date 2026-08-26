using Microsoft.AspNetCore.Routing;

namespace AutoPartsErp.Modules.Abstractions.Modules;

/// <summary>
/// A cohesive set of endpoints inside a module, for example everything under
/// <c>/api/catalog/parts</c>. Splitting endpoints this way keeps each file small and
/// makes it obvious where a new route belongs.
/// </summary>
public interface IEndpointGroup
{
    /// <summary>Maps this group's routes onto the module's route group.</summary>
    /// <param name="group">The module's route group, e.g. <c>/api/catalog</c>.</param>
    void Map(IEndpointRouteBuilder group);
}
