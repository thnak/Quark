using Microsoft.Extensions.Logging;
using Quark.Abstractions;
using Quark.Core.Actors;

namespace Quark.Examples.ActorQueries;

/// <summary>
/// A sample user actor for demonstrating queries.
/// </summary>
[Actor(Name = "UserActor")]
public class UserActor : ActorBase
{
    private string _email = string.Empty;
    private DateTimeOffset _createdAt = DateTimeOffset.UtcNow;

    public UserActor(string actorId) : base(actorId)
    {
    }

    public override Task OnActivateAsync(CancellationToken cancellationToken = default)
    {
        Console.WriteLine($"User {ActorId} activated");
        return base.OnActivateAsync(cancellationToken);
    }

    public override Task OnDeactivateAsync(CancellationToken cancellationToken = default)
    {
        Console.WriteLine($"User {ActorId} deactivated");
        return base.OnDeactivateAsync(cancellationToken);
    }

    public Task SetEmailAsync(string email)
    {
        _email = email;
        Console.WriteLine($"User {ActorId} email set to {email}");
        return Task.CompletedTask;
    }

    public Task<string> GetEmailAsync()
    {
        return Task.FromResult(_email);
    }
}

/// <summary>
/// A sample order actor for demonstrating queries.
/// </summary>
[Actor(Name = "OrderActor")]
public class OrderActor : ActorBase
{
    private decimal _total;
    private string _status = "pending";

    public OrderActor(string actorId) : base(actorId)
    {
    }

    public override Task OnActivateAsync(CancellationToken cancellationToken = default)
    {
        Console.WriteLine($"Order {ActorId} activated");
        return base.OnActivateAsync(cancellationToken);
    }

    public override Task OnDeactivateAsync(CancellationToken cancellationToken = default)
    {
        Console.WriteLine($"Order {ActorId} deactivated");
        return base.OnDeactivateAsync(cancellationToken);
    }

    public Task SetTotalAsync(decimal total)
    {
        _total = total;
        Console.WriteLine($"Order {ActorId} total set to {total}");
        return Task.CompletedTask;
    }

    public Task<decimal> GetTotalAsync()
    {
        return Task.FromResult(_total);
    }

    public Task UpdateStatusAsync(string status)
    {
        _status = status;
        Console.WriteLine($"Order {ActorId} status updated to {status}");
        return Task.CompletedTask;
    }
}

/// <summary>
/// A sample product actor for demonstrating queries.
/// </summary>
[Actor(Name = "ProductActor")]
public class ProductActor : ActorBase
{
    private string _name = string.Empty;
    private decimal _price;

    public ProductActor(string actorId) : base(actorId)
    {
    }

    public override Task OnActivateAsync(CancellationToken cancellationToken = default)
    {
        Console.WriteLine($"Product {ActorId} activated");
        return base.OnActivateAsync(cancellationToken);
    }

    public override Task OnDeactivateAsync(CancellationToken cancellationToken = default)
    {
        Console.WriteLine($"Product {ActorId} deactivated");
        return base.OnDeactivateAsync(cancellationToken);
    }

    public Task SetDetailsAsync(string name, decimal price)
    {
        _name = name;
        _price = price;
        Console.WriteLine($"Product {ActorId} details set: {name} @ ${price}");
        return Task.CompletedTask;
    }
}
