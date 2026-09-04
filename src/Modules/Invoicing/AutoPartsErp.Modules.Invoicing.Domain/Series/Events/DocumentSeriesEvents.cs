using AutoPartsErp.SharedKernel.Primitives;

namespace AutoPartsErp.Modules.Invoicing.Domain.Series.Events;

/// <summary>A series was created, before it has been declared to the AT.</summary>
/// <param name="SeriesId">The series.</param>
/// <param name="Type">What it numbers.</param>
/// <param name="Code">Its identifier.</param>
/// <param name="Year">The year it belongs to.</param>
public sealed record DocumentSeriesOpenedDomainEvent(
    DocumentSeriesId SeriesId,
    DocumentType Type,
    string Code,
    int Year) : DomainEvent;

/// <summary>
/// A series went live and can now issue.
/// <para>
/// Worth announcing: from this moment documents with legal weight start coming out of it, and
/// anything watching the accounting side wants to know a new run of numbers has begun.
/// </para>
/// </summary>
/// <param name="SeriesId">The series.</param>
/// <param name="Type">What it numbers.</param>
/// <param name="Code">Its identifier.</param>
/// <param name="ValidationCode">The AT code that will appear in every ATCUD it produces.</param>
public sealed record DocumentSeriesActivatedDomainEvent(
    DocumentSeriesId SeriesId,
    DocumentType Type,
    string Code,
    string ValidationCode) : DomainEvent;

/// <summary>A series was closed to new documents.</summary>
/// <param name="SeriesId">The series.</param>
/// <param name="Type">What it numbered.</param>
/// <param name="Code">Its identifier.</param>
/// <param name="IssuedCount">How many documents it issued, which is what the AT is told.</param>
public sealed record DocumentSeriesClosedDomainEvent(
    DocumentSeriesId SeriesId,
    DocumentType Type,
    string Code,
    int IssuedCount) : DomainEvent;
