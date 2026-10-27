using System.Data;
using System.Data.Common;
using System.Security.Claims;
using Microsoft.EntityFrameworkCore.Diagnostics;

namespace Servisyo.Web.Data;

/// Every time EF opens a database connection, this tells SQL Server WHO is using it (sp_set_session_context),
/// so the audit triggers can write the signed-in user's id and role into SYSTEM_LOGS.
/// The value lives only on that connection and is cleared when the connection goes back to the pool.
public sealed class AuditSessionInterceptor : DbConnectionInterceptor
{
    private const string Sql =
        "EXEC sp_set_session_context @key = N'servisyo_user_id', @value = @uid; " +
        "EXEC sp_set_session_context @key = N'servisyo_user_role', @value = @role;";

    private readonly IHttpContextAccessor _http;

    public AuditSessionInterceptor(IHttpContextAccessor http) => _http = http;

    public override void ConnectionOpened(DbConnection connection, ConnectionEndEventData eventData)
    {
        using var cmd = Build(connection);
        cmd?.ExecuteNonQuery();
    }

    public override async Task ConnectionOpenedAsync(DbConnection connection, ConnectionEndEventData eventData, CancellationToken cancellationToken = default)
    {
        await using var cmd = Build(connection);
        if (cmd is not null) await cmd.ExecuteNonQueryAsync(cancellationToken);
    }

    private DbCommand? Build(DbConnection connection)
    {
        var context = _http.HttpContext;
        if (context is null) return null;                     // app start-up (seeding): the triggers write "System"

        int? userId = null;
        var role = "Guest";                                   // a web request from someone not signed in (e.g. registering)
        var user = context.User;
        if (user.Identity?.IsAuthenticated == true && int.TryParse(user.FindFirstValue(ClaimTypes.NameIdentifier), out var id))
        {
            userId = id;
            role = user.FindFirstValue(ClaimTypes.Role) ?? "Guest";
        }

        var cmd = connection.CreateCommand();
        cmd.CommandText = Sql;

        var p1 = cmd.CreateParameter();
        p1.ParameterName = "@uid";
        p1.DbType = DbType.Int32;
        p1.Value = (object?)userId ?? DBNull.Value;
        cmd.Parameters.Add(p1);

        var p2 = cmd.CreateParameter();
        p2.ParameterName = "@role";
        p2.DbType = DbType.String;
        p2.Size = 50;
        p2.Value = role;
        cmd.Parameters.Add(p2);

        return cmd;
    }
}
