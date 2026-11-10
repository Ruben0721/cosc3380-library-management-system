using System.Data.Common;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Infrastructure;

namespace Project.Data;

public sealed record InventorySchema(bool HasRetiredAt)
{
    public static InventorySchema Read(DbConnection connection)
    {
        using var command = connection.CreateCommand();
        command.CommandText = """
            SELECT EXISTS (
                SELECT 1 FROM INFORMATION_SCHEMA.COLUMNS
                WHERE TABLE_SCHEMA = DATABASE()
                    AND TABLE_NAME = 'Inventory'
                    AND COLUMN_NAME = 'RetiredAt'
            )
            """;
        return new InventorySchema(Convert.ToBoolean(command.ExecuteScalar()));
    }
}

public sealed class InventorySchemaModelCacheKeyFactory : IModelCacheKeyFactory
{
    public object Create(DbContext context, bool designTime) =>
        (context.GetType(), ((LibraryDbContext)context).InventoryRetirementEnabled, designTime);
}
