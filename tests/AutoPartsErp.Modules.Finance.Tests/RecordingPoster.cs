using AutoPartsErp.Modules.Finance.Application.Ledger;
using AutoPartsErp.Modules.Finance.Domain;
using AutoPartsErp.Modules.Finance.Domain.Ledger;
using AutoPartsErp.SharedKernel.Results;
using AutoPartsErp.SharedKernel.ValueObjects;

namespace AutoPartsErp.Modules.Finance.Tests;

/// <summary>One fact as it was handed to the ledger.</summary>
/// <param name="FactType">What happened.</param>
/// <param name="Reference">The document behind it.</param>
/// <param name="OccurredOn">The day it belongs to.</param>
/// <param name="Description">What it is.</param>
/// <param name="Amounts">What it carried.</param>
/// <param name="Saved">
/// True when the handler committed the transaction itself. An integration event handler owns its
/// scope and does; a domain event handler runs inside the save that caused it and must not.
/// </param>
internal sealed record HandedFact(
    string FactType,
    string Reference,
    DateOnly OccurredOn,
    string Description,
    IReadOnlyDictionary<string, Money> Amounts,
    bool Saved);

/// <summary>
/// A poster that writes nothing and remembers what it was asked to post.
/// <para>
/// Shared by the handler tests, which are all about the same question — what each event turns into
/// before the rules and the chart get involved — and not about the posting itself, which has its
/// own tests.
/// </para>
/// </summary>
internal sealed class RecordingPoster : ILedgerPoster
{
    public List<HandedFact> Facts { get; } = [];

    public Task<Result<JournalEntryId?>> PostFactAsync(
        string factType,
        string reference,
        DateOnly occurredOn,
        string description,
        IReadOnlyDictionary<string, Money> amounts,
        CancellationToken cancellationToken = default) =>
        Remember(factType, reference, occurredOn, description, amounts, saved: true);

    public Task<Result<JournalEntryId?>> RecordFactAsync(
        string factType,
        string reference,
        DateOnly occurredOn,
        string description,
        IReadOnlyDictionary<string, Money> amounts,
        CancellationToken cancellationToken = default) =>
        Remember(factType, reference, occurredOn, description, amounts, saved: false);

    public Task<Result> TryPostAsync(
        FactPosting posting, CancellationToken cancellationToken = default) =>
        Task.FromResult(Result.Success());

    private Task<Result<JournalEntryId?>> Remember(
        string factType,
        string reference,
        DateOnly occurredOn,
        string description,
        IReadOnlyDictionary<string, Money> amounts,
        bool saved)
    {
        Facts.Add(new HandedFact(
            factType, reference, occurredOn, description, amounts, saved));

        return Task.FromResult(Result.Success<JournalEntryId?>(null));
    }
}
