using Bank.GrainInterfaces;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Quark.Client;
using Quark.Client.Tcp;
using Quark.Core;
using Quark.Core.Abstractions.Hosting;

IHost host = Host.CreateDefaultBuilder(args)
    .UseQuarkClient(client =>
    {
        client.UseLocalhostGateway(gatewayPort: 30005);
        client.Services.AddBankGrainInterfacesGrainProxies();
    })
    .Build();

await host.StartAsync();

IGrainFactory grains = host.Services.GetRequiredService<IGrainFactory>();

Console.Write("Account id (e.g. alice): ");
string id = Console.ReadLine()?.Trim() is { Length: > 0 } s ? s : "alice";

IAccountGrain account = grains.GetGrain<IAccountGrain>(id);
IProfileGrain profile = grains.GetGrain<IProfileGrain>(id);
ILedgerGrain ledger = grains.GetGrain<ILedgerGrain>(id);
IVaultGrain vault = grains.GetGrain<IVaultGrain>(id);
IStatementGrain statement = grains.GetGrain<IStatementGrain>(id);

PrintHelp();
Console.WriteLine();
Console.WriteLine($"Tip: stop the server (Ctrl+C) and restart it, then run 'balance'/'history' here —");
Console.WriteLine($"     in-memory state resets with the process; switch to Redis storage to keep it.");
Console.WriteLine();

while (true)
{
    Console.Write($"{id}> ");
    string input = Console.ReadLine()?.Trim() ?? "";
    if (input.Equals("quit", StringComparison.OrdinalIgnoreCase)) break;
    if (input.Length == 0) continue;

    string[] parts = input.Split(' ', 2, StringSplitOptions.RemoveEmptyEntries);
    string verb = parts[0].ToLowerInvariant();
    string arg = parts.Length > 1 ? parts[1] : "";

    try
    {
        switch (verb)
        {
            // --- Pattern 1: persistent activation memory (account balance) ---
            case "deposit":
                Console.WriteLine($"Balance: {await account.DepositAsync(decimal.Parse(arg)):C}");
                break;
            case "withdraw":
                Console.WriteLine($"Balance: {await account.WithdrawAsync(decimal.Parse(arg)):C}");
                break;
            case "balance":
                Console.WriteLine($"Balance: {await account.GetBalanceAsync():C}  " +
                                  $"({await account.GetTransactionCountAsync()} transactions)");
                break;

            // --- Pattern 2: named persistent state (profile) ---
            case "profile":
            {
                string[] pa = arg.Split(' ', 2, StringSplitOptions.RemoveEmptyEntries);
                if (pa.Length == 2) { await profile.UpdateAsync(pa[0], pa[1]); Console.WriteLine("Profile saved."); }
                else Console.WriteLine("Usage: profile <name> <email>");
                break;
            }
            case "whoami":
                Console.WriteLine(await profile.DescribeAsync());
                break;

            // --- Pattern 3: event sourcing (ledger) ---
            case "credit":
            {
                (decimal amt, string note) = ParseAmountNote(arg);
                Console.WriteLine($"Ledger balance: {await ledger.CreditAsync(amt, note):C}");
                break;
            }
            case "debit":
            {
                (decimal amt, string note) = ParseAmountNote(arg);
                Console.WriteLine($"Ledger balance: {await ledger.DebitAsync(amt, note):C}");
                break;
            }
            case "ledger":
                Console.WriteLine($"Ledger balance: {await ledger.GetBalanceAsync():C}  " +
                                  $"(version {await ledger.GetVersionAsync()})");
                break;
            case "history":
                Console.WriteLine(await ledger.GetHistoryAsync());
                break;

            // --- Pattern 4: eager activation memory (DI-loaded, pinned rate) ---
            case "vault":
                Console.WriteLine($"Principal: {await vault.DepositAsync(decimal.Parse(arg)):C}");
                break;
            case "accrue":
                Console.WriteLine($"Principal after interest: {await vault.AccrueAsync(int.Parse(arg)):C}");
                break;
            case "rate":
                Console.WriteLine(await vault.GetPinnedRateAsync());
                break;

            // --- Pattern 5: managed activation memory (lazy async resource + cleanup) ---
            case "note":
                await statement.AddLineAsync(arg);
                Console.WriteLine($"Buffered (init count: {await statement.GetInitCountAsync()}).");
                break;
            case "statement":
                Console.WriteLine(await statement.RenderAsync());
                break;
            case "close":
                await statement.CloseAsync();
                Console.WriteLine("Statement grain deactivated — watch the server console for the flush.");
                break;

            case "help":
                PrintHelp();
                break;
            default:
                Console.WriteLine("Unknown command. Type 'help'.");
                break;
        }
    }
    catch (Exception ex)
    {
        Console.WriteLine($"! {ex.Message}");
    }
}

await host.StopAsync();
return;

static (decimal amount, string note) ParseAmountNote(string arg)
{
    string[] p = arg.Split(' ', 2, StringSplitOptions.RemoveEmptyEntries);
    if (p.Length == 0) throw new FormatException("Usage: credit|debit <amount> [note]");
    return (decimal.Parse(p[0]), p.Length > 1 ? p[1] : "");
}

static void PrintHelp()
{
    Console.WriteLine("""
        Commands (all scoped to the chosen account id):
          Persistent activation memory (write-through balance):
            deposit <amount>          add money, persists immediately
            withdraw <amount>         remove money (fails if insufficient)
            balance                   read balance from activation memory
          Named persistent state (Orleans-style [PersistentState]):
            profile <name> <email>    save the account holder profile
            whoami                    describe the saved profile
          Event sourcing (JournaledGrain — append-only log, replayed on activation):
            credit <amount> [note]    append a Credited event
            debit  <amount> [note]    append a Debited event
            ledger                    show derived balance + version (event count)
            history                   show the full audit trail
          Eager activation memory (DI-loaded rate, pinned at activation, sync access):
            vault <amount>            add principal to the interest-bearing vault
            accrue <days>            apply the pinned daily rate over N days
            rate                      show the rate pinned from DI at activation
          Managed activation memory (lazy async resource, flushed on deactivation):
            note <text>               buffer a statement line (inits the resource lazily)
            statement                 render the buffered statement
            close                     deactivate the grain (server console shows the flush)
          help                        show this help
          quit                        exit
        """);
}
