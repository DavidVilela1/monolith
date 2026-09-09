using AutoPartsErp.Modules.Catalog.Infrastructure.Persistence;
using AutoPartsErp.Persistence.Inbox;
using AutoPartsErp.Persistence.Outbox;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;

namespace AutoPartsErp.IntegrationTests.Tests;

/// <summary>
/// Claiming outbox messages, and deleting the ones nobody needs any more.
/// <para>
/// Both are single statements PostgreSQL executes and EF Core cannot express, which puts them in
/// exactly the category this suite exists for: nothing about <c>FOR UPDATE SKIP LOCKED</c> or a
/// bounded delete can fail at compile time, and the first place either of them can be wrong is
/// against a real server.
/// </para>
/// <para>
/// Rows are dated to 1990 so that they sort ahead of whatever the rest of the suite has published
/// into the same table, and deleted again at the end of each test so the next one starts from the
/// same place. The tests share a collection and therefore never run at the same time.
/// </para>
/// </summary>
[Collection(ErpCollection.Name)]
public sealed class OutboxHousekeepingTests
{
    private static readonly Guid Tenant = new("00000000-0000-0000-0000-000000000001");
    private static readonly DateTimeOffset Ancient = new(1990, 1, 1, 0, 0, 0, TimeSpan.Zero);
    private static readonly DateTimeOffset Now = new(2026, 9, 8, 9, 0, 0, TimeSpan.Zero);
    private static readonly TimeSpan Lease = TimeSpan.FromMinutes(5);

    private readonly ErpFixture _fixture;

    /// <summary>Initializes the tests.</summary>
    public OutboxHousekeepingTests(ErpFixture fixture)
    {
        _fixture = fixture;
    }

    /// <summary>
    /// The property that lets a second host exist. A batch one sweep has taken is not offered to
    /// the next one, and it becomes available again on its own once the lease runs out — which is
    /// what makes a host that was killed mid-batch recoverable without anybody noticing it died.
    /// </summary>
    [Fact]
    public async Task A_claimed_batch_is_not_offered_again_until_its_lease_runs_out()
    {
        Guid[] ids = await WriteMessagesAsync(3);

        try
        {
            Guid[] first = await ClaimAsync(3, Now);
            first.Should().BeEquivalentTo(ids);

            Guid[] second = await ClaimAsync(3, Now.AddMinutes(1));
            second.Should().NotIntersectWith(ids);

            Guid[] afterLease = await ClaimAsync(3, Now.Add(Lease).AddMinutes(1));
            afterLease.Should().BeEquivalentTo(ids);
        }
        finally
        {
            await DeleteMessagesAsync(ids);
        }
    }

    /// <summary>
    /// A message that has run out of attempts is left where it is, with its error, for a person.
    /// A sweep that kept picking it up would bury the log in the same failure for ever.
    /// </summary>
    [Fact]
    public async Task A_message_that_has_given_up_is_never_claimed()
    {
        Guid[] ids = await WriteMessagesAsync(1, message => message.Attempts = 10);

        try
        {
            Guid[] claimed = await ClaimAsync(10, Now);

            claimed.Should().NotIntersectWith(ids);
        }
        finally
        {
            await DeleteMessagesAsync(ids);
        }
    }

    /// <summary>
    /// Housekeeping deletes what was delivered and keeps what was not, however old it is. The
    /// second half is the one worth a test: an undelivered message is the only row in this table
    /// somebody still has to act on, and a retention job that swept it away would be deleting the
    /// evidence that anything went wrong.
    /// </summary>
    [Fact]
    public async Task Retention_deletes_delivered_messages_and_keeps_undelivered_ones()
    {
        Guid[] delivered = await WriteMessagesAsync(
            1, message => message.ProcessedAtUtc = Ancient.AddMinutes(1));

        Guid[] undelivered = await WriteMessagesAsync(1);

        try
        {
            int deleted = await _fixture.Application.AsTenantAsync(
                Tenant,
                services => services
                    .GetRequiredService<CatalogDbContext>()
                    .PurgeProcessedOutboxAsync(Ancient.AddDays(1), 1000));

            deleted.Should().Be(1);

            Guid[] left = await ExistingAsync([.. delivered, .. undelivered]);

            left.Should().BeEquivalentTo(undelivered);
        }
        finally
        {
            await DeleteMessagesAsync([.. delivered, .. undelivered]);
        }
    }

    /// <summary>The same for the consumer's side of the pipe.</summary>
    [Fact]
    public async Task Retention_deletes_inbox_records_older_than_the_cut_off()
    {
        var old = new InboxMessage
        {
            MessageId = Guid.NewGuid(),
            HandlerName = "IntegrationTests.Old",
            HandledAtUtc = Ancient,
        };

        var recent = new InboxMessage
        {
            MessageId = Guid.NewGuid(),
            HandlerName = "IntegrationTests.Recent",
            HandledAtUtc = Now,
        };

        await _fixture.Application.AsTenantAsync(Tenant, async services =>
        {
            CatalogDbContext context = services.GetRequiredService<CatalogDbContext>();
            context.InboxMessages.AddRange(old, recent);
            await context.SaveChangesAsync();
        });

        try
        {
            int deleted = await _fixture.Application.AsTenantAsync(
                Tenant,
                services => services
                    .GetRequiredService<CatalogDbContext>()
                    .PurgeHandledInboxAsync(Ancient.AddDays(1), 1000));

            deleted.Should().Be(1);

            bool recentSurvived = await _fixture.Application.AsTenantAsync(
                Tenant,
                services => services
                    .GetRequiredService<CatalogDbContext>()
                    .InboxMessages
                    .AnyAsync(message => message.MessageId == recent.MessageId));

            recentSurvived.Should().BeTrue();
        }
        finally
        {
            await _fixture.Application.AsTenantAsync(Tenant, async services =>
            {
                CatalogDbContext context = services.GetRequiredService<CatalogDbContext>();

                await context.InboxMessages
                    .Where(message => message.MessageId == old.MessageId
                        || message.MessageId == recent.MessageId)
                    .ExecuteDeleteAsync();
            });
        }
    }

    private async Task<Guid[]> WriteMessagesAsync(int count, Action<OutboxMessage>? adjust = null)
    {
        var written = new List<Guid>(count);

        await _fixture.Application.AsTenantAsync(Tenant, async services =>
        {
            CatalogDbContext context = services.GetRequiredService<CatalogDbContext>();

            for (int index = 0; index < count; index++)
            {
                var message = new OutboxMessage
                {
                    Id = Guid.NewGuid(),
                    Type = "AutoPartsErp.IntegrationTests.Housekeeping",
                    Content = "{}",
                    TenantId = Tenant,
                    OccurredAtUtc = Ancient.AddSeconds(index),
                };

                adjust?.Invoke(message);

                context.OutboxMessages.Add(message);
                written.Add(message.Id);
            }

            await context.SaveChangesAsync();
        });

        return [.. written];
    }

    private Task<Guid[]> ClaimAsync(int batchSize, DateTimeOffset now) =>
        _fixture.Application.AsTenantAsync(
            Tenant,
            services => services
                .GetRequiredService<CatalogDbContext>()
                .ClaimOutboxMessagesAsync(batchSize, maxAttempts: 10, now, Lease));

    private Task<Guid[]> ExistingAsync(Guid[] ids) =>
        _fixture.Application.AsTenantAsync(
            Tenant,
            async services => await services
                .GetRequiredService<CatalogDbContext>()
                .OutboxMessages
                .Where(message => ids.Contains(message.Id))
                .Select(message => message.Id)
                .ToArrayAsync());

    private Task DeleteMessagesAsync(Guid[] ids) =>
        _fixture.Application.AsTenantAsync(Tenant, async services =>
        {
            await services
                .GetRequiredService<CatalogDbContext>()
                .OutboxMessages
                .Where(message => ids.Contains(message.Id))
                .ExecuteDeleteAsync();
        });
}
