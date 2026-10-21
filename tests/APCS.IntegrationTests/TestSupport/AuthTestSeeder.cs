using APCS.Common.Constants;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Npgsql;

namespace APCS.IntegrationTests.TestSupport;

/// <summary>
/// Inserts the role and account rows an authorization test needs, straight through Npgsql.
/// </summary>
/// <remarks>
/// <para>
/// The committed bootstrap dump is schema-only, so <c>roles</c>, <c>users</c>, and
/// <c>user_roles</c> start empty. Raw SQL is used rather than EF Core because the tests need rows
/// whose shape a database-first entity cannot express, most importantly a role whose
/// <c>name</c> deliberately differs from its <c>code</c>.
/// </para>
/// <para>
/// Every identifier is generated per call and every insert is idempotent on the natural key, so a
/// suite that runs without Respawn against a shared database still leaves the rows it owns alone.
/// </para>
/// </remarks>
public sealed class AuthTestSeeder(string connectionString)
{
    /// <summary>
    /// Creates a seeder bound to the database the hosted API is actually running against.
    /// </summary>
    /// <remarks>
    /// The endpoint is published to configuration before the host boots, so reading it back from the
    /// host's own services is what keeps a fixture pointed at the same database the application uses
    /// instead of at a connection string the test assumed.
    /// </remarks>
    /// <param name="services">The hosted application's service provider.</param>
    /// <returns>A seeder writing to the hosted database.</returns>
    /// <exception cref="InvalidOperationException">Thrown when no connection string is configured.</exception>
    public static AuthTestSeeder Create(IServiceProvider services)
    {
        ArgumentNullException.ThrowIfNull(services);

        var connectionString = services
            .GetRequiredService<IConfiguration>()
            .GetConnectionString(ConfigurationKeys.ConnectionStrings.DefaultConnection);

        return new AuthTestSeeder(
            connectionString ?? throw new InvalidOperationException(
                "The hosted application has no default connection string, so rows cannot be seeded into its database."));
    }

    /// <summary>
    /// Creates or updates a role.
    /// </summary>
    /// <param name="code">The value the JWT claim is built from.</param>
    /// <param name="name">The value the admin role editor matches on.</param>
    /// <param name="isSystemRole">Whether the role is flagged as a built-in one.</param>
    /// <returns>The role identifier.</returns>
    public async Task<Guid> SeedRoleAsync(string code, string name, bool isSystemRole = true)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(code);
        ArgumentException.ThrowIfNullOrWhiteSpace(name);

        await using var connection = await OpenAsync();
        await using var command = new NpgsqlCommand(
            """
            INSERT INTO roles (id, code, name, is_system_role, created_at)
            VALUES (@id, @code, @name, @isSystemRole, now())
            ON CONFLICT (code) DO UPDATE SET name = EXCLUDED.name
            RETURNING id;
            """,
            connection);

        command.Parameters.AddWithValue("id", Guid.NewGuid());
        command.Parameters.AddWithValue("code", code);
        command.Parameters.AddWithValue("name", name);
        command.Parameters.AddWithValue("isSystemRole", isSystemRole);

        return (Guid)(await command.ExecuteScalarAsync())!;
    }

    /// <summary>
    /// Creates an active, email-verified account so a login test starts from a usable identity.
    /// </summary>
    /// <param name="email">The unique sign-in address.</param>
    /// <param name="fullName">The display name.</param>
    /// <param name="passwordHash">
    /// A hash produced by the same <c>IPasswordHasher</c> the application registers. Supplied by the
    /// caller so the test never has to reimplement the hash format.
    /// </param>
    /// <param name="userId">
    /// The identifier to force, or <see langword="null"/> to generate one. Forcing it lets a test
    /// mint a token whose subject matches the seeded row.
    /// </param>
    /// <returns>The account identifier.</returns>
    public async Task<Guid> SeedActiveUserAsync(
        string email,
        string fullName,
        string passwordHash,
        Guid? userId = null)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(email);
        ArgumentException.ThrowIfNullOrWhiteSpace(fullName);
        ArgumentException.ThrowIfNullOrWhiteSpace(passwordHash);

        var id = userId ?? Guid.NewGuid();

        await using var connection = await OpenAsync();
        await using var command = new NpgsqlCommand(
            """
            INSERT INTO users (id, email, password_hash, full_name, account_status, email_verified, email_verified_at)
            VALUES (@id, @email, @passwordHash, @fullName, @status, true, now())
            ON CONFLICT (id) DO UPDATE
                SET password_hash = EXCLUDED.password_hash,
                    full_name = EXCLUDED.full_name,
                    account_status = EXCLUDED.account_status,
                    email_verified = true,
                    deleted_at = NULL;
            """,
            connection);

        command.Parameters.AddWithValue("id", id);
        command.Parameters.AddWithValue("email", email);
        command.Parameters.AddWithValue("passwordHash", passwordHash);
        command.Parameters.AddWithValue("fullName", fullName);
        command.Parameters.AddWithValue("status", AccountStatuses.Active);

        await command.ExecuteNonQueryAsync();
        return id;
    }

    /// <summary>
    /// Grants a role to an account.
    /// </summary>
    /// <param name="userId">The account identifier.</param>
    /// <param name="roleId">The role identifier.</param>
    /// <returns>A task that completes once the grant exists.</returns>
    public async Task GrantRoleAsync(Guid userId, Guid roleId)
    {
        await using var connection = await OpenAsync();
        await using var command = new NpgsqlCommand(
            """
            INSERT INTO user_roles (user_id, role_id, granted_at, revoked_at)
            VALUES (@userId, @roleId, now(), NULL)
            ON CONFLICT (user_id, role_id) DO UPDATE SET revoked_at = NULL;
            """,
            connection);

        command.Parameters.AddWithValue("userId", userId);
        command.Parameters.AddWithValue("roleId", roleId);

        await command.ExecuteNonQueryAsync();
    }

    /// <summary>
    /// Removes the account rows a test created, so a shared database is left as it was found.
    /// </summary>
    /// <param name="userIds">The account identifiers to delete.</param>
    /// <returns>A task that completes once the rows are gone.</returns>
    public async Task DeleteUsersAsync(IReadOnlyCollection<Guid> userIds)
    {
        ArgumentNullException.ThrowIfNull(userIds);

        if (userIds.Count == 0)
        {
            return;
        }

        await using var connection = await OpenAsync();
        await using var command = new NpgsqlCommand("DELETE FROM users WHERE id = ANY(@ids);", connection);
        command.Parameters.AddWithValue("ids", userIds.ToArray());

        await command.ExecuteNonQueryAsync();
    }

    /// <summary>
    /// Removes the role rows a test created, together with every grant that points at them.
    /// </summary>
    /// <remarks>
    /// <c>user_roles</c> references a role with <c>ON DELETE RESTRICT</c>, so a role cannot be
    /// removed while it is still granted. A test owns every grant to a role it seeded, and clearing
    /// them is what lets the role go.
    /// </remarks>
    /// <param name="codes">The role codes to delete.</param>
    /// <returns>A task that completes once the rows are gone.</returns>
    public async Task DeleteRolesAsync(IReadOnlyCollection<string> codes)
    {
        ArgumentNullException.ThrowIfNull(codes);

        if (codes.Count == 0)
        {
            return;
        }

        await using var connection = await OpenAsync();
        await using var transaction = await connection.BeginTransactionAsync();

        await using (var grantCommand = new NpgsqlCommand(
            "DELETE FROM user_roles WHERE role_id IN (SELECT id FROM roles WHERE code = ANY(@codes));",
            connection,
            transaction))
        {
            grantCommand.Parameters.AddWithValue("codes", codes.ToArray());
            await grantCommand.ExecuteNonQueryAsync();
        }

        await using (var roleCommand = new NpgsqlCommand("DELETE FROM roles WHERE code = ANY(@codes);", connection, transaction))
        {
            roleCommand.Parameters.AddWithValue("codes", codes.ToArray());
            await roleCommand.ExecuteNonQueryAsync();
        }

        await transaction.CommitAsync();
    }

    /// <summary>
    /// Reads the role claims the repository hands to the token factory for an account.
    /// </summary>
    /// <remarks>
    /// This mirrors <c>AccountRepository.GetActiveRoleCodesAsync</c>, which projects
    /// <c>Role.Code</c> rather than <c>Role.Name</c>. Asserting against it isolates the claim source
    /// from the login pipeline that consumes it.
    /// </remarks>
    /// <param name="userId">The account identifier.</param>
    /// <returns>The active role codes, in no guaranteed order.</returns>
    public async Task<IReadOnlyList<string>> ReadActiveRoleCodesAsync(Guid userId)
    {
        await using var connection = await OpenAsync();
        await using var command = new NpgsqlCommand(
            """
            SELECT r.code
            FROM user_roles ur
            INNER JOIN roles r ON r.id = ur.role_id
            WHERE ur.user_id = @userId AND ur.revoked_at IS NULL
            ORDER BY r.code;
            """,
            connection);

        command.Parameters.AddWithValue("userId", userId);

        var codes = new List<string>();
        await using var reader = await command.ExecuteReaderAsync();
        while (await reader.ReadAsync())
        {
            codes.Add(reader.GetString(0));
        }

        return codes;
    }

    private async Task<NpgsqlConnection> OpenAsync()
    {
        var connection = new NpgsqlConnection(connectionString);
        await connection.OpenAsync();
        return connection;
    }
}
