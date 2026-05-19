 What did Claude get right?
 Claude was right when it gave me idea of new feature for pricing. It gave proper strategic pattern for pricing by using different strategies and keeping the service small. The idea was good because it would have lead to errors if changes were made with respect to orderitems and price from customers.

 Where would you have caught a bug it introduced?
 It tried to introduce CustomerTier into Order.cs without checking my actual model. I think it assumed my code.I caught that bug when I uploaded my actual code on claude and verified it. 

 What did Copilot save you?
 it was genuinely fast at generating the test stubs from comments. Especially the negative case like invalid quantity—it would’ve taken me a few minutes to set up the structure, think through edge cases, and write assertions cleanly. Copilot cut that down to seconds, which is useful when you’re trying to keep speed.

 Where did it suggest something subtly wrong? 
 One simple issue was that it placed the tests inside the wrong context (inside OrderApiFactory instead of OrderIntegrationTests). The code still compiled and tests were discovered, so nothing looked broken at first glance. But the execution context was wrong, which meant the results weren’t actually validating the right layer. That’s the dangerous type of bug—you don’t get red errors, you get misleading green builds.

 At 2 AM IST tomorrow debugging prod, which one do you reach for first? 
 I think I'll use copilot because it was fast and runs automatically with your project. It is better to use copilot.Claude give proper structural code but requires lengthy prompts and also it assumes certain codes which can lead to bugs at runtime.

