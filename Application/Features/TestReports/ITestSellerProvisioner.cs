using System;
using System.Threading;
using System.Threading.Tasks;
using APCS.Common.Models;

namespace APCS.Application.Features.TestReports;

/// <summary>
/// The account the end-to-end suite signs in as.
/// </summary>
/// <param name="Email">The address of the account.</param>
/// <param name="Password">The password of the account, known only for the length of one run.</param>
public sealed record TestSellerCredentials(string Email, string Password);

/// <summary>
/// Keeps the seller account the end-to-end suite signs in as.
/// </summary>
public interface ITestSellerProvisioner
{
    /// <summary>
    /// Makes sure the seller account exists, is verified and has a fresh password.
    /// </summary>
    Task<TestSellerCredentials> EnsureAsync(CancellationToken cancellationToken = default);

    /// <summary>
    /// Removes the seller account and everything the suite left on it.
    /// </summary>
    Task<Result> RemoveAsync(CancellationToken cancellationToken = default);
}
