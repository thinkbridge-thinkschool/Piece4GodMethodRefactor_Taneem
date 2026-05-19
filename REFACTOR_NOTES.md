Smells in OrderController.cs

1) In line 39 i<=Orders.count, it might show index out of bounds exception.
### Consequence
IndexOutOfRangeException
### Fix
Use i<Orders.count

2) No Pagination
GetAllOrders returned every record.
### Consequence
Memory and performance issues on large databases.
### Fix
Add:
page
pageSize
paged response DTO

3) Empty Catch Blocks:
Many places used
```csharp
catch { }
### Consequence
Errors become invisible and debugging becomes difficult.
### Fix
Use proper exception handling and logging.

4) Controller contains validation, business logic, database access, email sending, audit logging
### Consequence
Code becomes huge and difficult to maintain.
### Fix
Move logic into:
- Services
- Repositories
- DTOs
- Helpers

5) No Proper HTTP Status Codes:
Returned null, error strings, anonymous objects instead of proper API responses.
### Consequence
Frontend cannot properly detect errors.
Fix
Use
404 NotFound
400 BadRequest
422 UnprocessableEntity
ProblemDetails

6) Entity Models In Same File:
Controller, DbContext, entities all inside one file.
### Consequence
Poor project structure and readability.
Fix
Separate folders:
Controllers
Models
Data
Services
Repositories

7) Local Time Instead Of UTC
Used:
DateTime.Now
### Consequence
Timezone inconsistency across systems.
### Fix
Use: DateTimeOffset.UtcNow

8) Hardcoded Magic Strings
Status values written directly:
"Pending"
"Shipped"
### Consequence
Typos and inconsistency.
### Fix
Create centralized OrderStatus constants.

9)No Repository Pattern
Controller directly accessed EF Core DbContext.
### Consequence
Tight coupling and difficult testing.
### Fix
Add Repository abstraction layer.

10) Incorrect Average Calculation
Average divided by:
Count - 1
### Consequence
Incorrect report values.
### Fix
Divide by actual Count.

11) Using Double For Money
Money stored using: double
### Consequence
Floating point precision errors.
### Fix
Use: decimal
for currency values.

12)No Validation
Invalid data continued execution.
### Consequence
Bad data gets stored in database.
### Fix
Use: DataAnnotations
Model validation
DTO validation