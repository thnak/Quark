using Quark.Examples.ContextRegistration;

/*
 * Example: Context-Based Actor Registration
 * ==========================================
 * 
 * This example demonstrates how to use QuarkActorContext to register
 * actor interfaces for proxy generation, even when those interfaces
 * don't inherit from IQuarkActor.
 * 
 * Use Case:
 * ---------
 * You're working with an external library that defines actor interfaces,
 * but you can't modify the library to make them inherit from IQuarkActor.
 * 
 * Solution:
 * ---------
 * Use [QuarkActorContext] and [QuarkActor(typeof(...))] attributes to
 * explicitly register interfaces for proxy generation.
 * 
 * Key Points:
 * -----------
 * 1. ICalculatorService does NOT inherit from IQuarkActor
 * 2. ExternalActorContext uses [QuarkActorContext] to register it
 * 3. The source generator creates proxies at compile-time
 * 4. Generated proxies automatically implement IQuarkActor
 * 5. You can use GetActor<ICalculatorService>() normally
 */

Console.WriteLine("=== Context-Based Actor Registration Example ===\n");

Console.WriteLine("Key Features:");
Console.WriteLine("1. ICalculatorService does NOT inherit from IQuarkActor");
Console.WriteLine("2. Registered via ExternalActorContext");
Console.WriteLine("3. Proxy generation works automatically at compile-time");
Console.WriteLine("4. Generated proxy implements both ICalculatorService AND IQuarkActor");
Console.WriteLine();

Console.WriteLine("Benefits:");
Console.WriteLine("- Works with external library interfaces you can't modify");
Console.WriteLine("- Explicit control over what gets proxy generation");
Console.WriteLine("- Same pattern as System.Text.Json's JsonSerializerContext");
Console.WriteLine("- Full AOT compatibility (zero reflection)");
Console.WriteLine();

Console.WriteLine("Example Usage in Real Application:");
Console.WriteLine("----------------------------------");
Console.WriteLine();
Console.WriteLine("// Client side:");
Console.WriteLine("var calculator = client.GetActor<ICalculatorService>(\"calc-1\");");
Console.WriteLine("var sum = await calculator.AddAsync(5, 3);");
Console.WriteLine("var product = await calculator.MultiplyAsync(4, 7);");
Console.WriteLine("var history = await calculator.GetHistoryAsync();");
Console.WriteLine();

Console.WriteLine("Check the generated files in:");
Console.WriteLine("obj/Generated/Quark.Generators/Quark.Generators.ProxySourceGenerator/");
Console.WriteLine("- ICalculatorServiceMessages.g.cs (Protobuf contracts)");
Console.WriteLine("- ICalculatorServiceProxy.g.cs (Client proxy)");
Console.WriteLine("- ActorProxyFactory.g.cs (Factory registration)");
Console.WriteLine();

Console.WriteLine("=== Example Complete ===");
