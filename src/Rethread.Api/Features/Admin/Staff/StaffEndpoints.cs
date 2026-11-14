using FluentValidation;
using Microsoft.AspNetCore.Http.HttpResults;
using Microsoft.EntityFrameworkCore;
using Rethread.Api.Auth;
using Rethread.Api.Common;
using Rethread.Api.Data;
using Rethread.Api.Data.Entities;

namespace Rethread.Api.Features.Admin.Staff;

public sealed record StaffMember(string Id, string Name, string Email, StaffRole Role, AccountStatus Status,
    DateTime? LastSignInAt)
{
    public static StaffMember From(Data.Entities.Staff s) => new(s.Id, s.Name, s.Email, s.Role, s.Status, s.LastSignInAt);
}

public sealed record InviteRequest(string Name, string Email, StaffRole Role);

public sealed record StaffChangeRequest(StaffRole? Role, AccountStatus? Status);

public sealed class InviteRequestValidator : AbstractValidator<InviteRequest>
{
    public InviteRequestValidator()
    {
        RuleFor(x => x.Name).NotEmpty().MaximumLength(80);
        RuleFor(x => x.Email).NotEmpty().EmailAddress().MaximumLength(254);
    }
}

/// <summary>Staff accounts (Admin). Invites email a set-password link that lasts three days.</summary>
public static class StaffEndpoints
{
    public static readonly TimeSpan InviteLifetime = TimeSpan.FromDays(3);

    public static void MapStaff(this RouteGroupBuilder admin)
    {
        var staff = admin.MapGroup("/staff").AdminOnly();
        staff.MapGet("", List);
        staff.MapPost("", Invite).Validate<InviteRequest>();
        staff.MapPatch("/{id}", Change);
    }

    private static async Task<Ok<ItemList<StaffMember>>> List(AppDbContext db, CancellationToken ct)
    {
        var staff = await db.Staff.AsNoTracking().OrderBy(s => s.CreatedAt).ThenBy(s => s.Id).ToListAsync(ct);
        return TypedResults.Ok(new ItemList<StaffMember>(staff.Select(StaffMember.From).ToList()));
    }

    private static async Task<Created<StaffMember>> Invite(InviteRequest request, CurrentUser user, AppDbContext db,
        IEmailSender email, IConfiguration config, TimeProvider time, CancellationToken ct)
    {
        var inviter = await user.StaffAsync(ct);
        var address = request.Email.Trim().ToLowerInvariant();
        if (await db.Staff.AnyAsync(s => s.Email == address, ct))
            throw ApiException.Conflict("EMAIL_IN_USE", "Someone already has a staff account with this email.");

        var token = Secrets.Token("inv");
        var now = time.UtcNow();
        var member = new Data.Entities.Staff
        {
            Id = Ids.New("stf"),
            Name = request.Name.Trim(),
            Email = address,
            Role = request.Role,
            InviteTokenHash = Secrets.Hash(token),
            InviteExpiresAt = now + InviteLifetime,
            CreatedAt = now,
        };
        db.Staff.Add(member);
        await db.SaveChangesAsync(ct);

        var portal = (config["Portal:BaseUrl"] ?? "http://localhost:3000").TrimEnd('/');
        await email.SendAsync(address, "You're invited to the ReThread portal",
            $"{inviter.Name} added you to the ReThread portal. Set your password within 3 days:\n" +
            $"{portal}/set-password?token={Uri.EscapeDataString(token)}", ct);
        return TypedResults.Created($"/api/v1/admin/staff/{member.Id}", StaffMember.From(member));
    }

    /// <summary>Change a role or suspend. Never your own, so there's always another admin to undo it.</summary>
    private static async Task<Ok<StaffMember>> Change(string id, StaffChangeRequest request, CurrentUser user,
        AppDbContext db, TokenService tokens, CancellationToken ct)
    {
        var me = await user.StaffAsync(ct);
        if (me.Id == id)
            throw ApiException.Conflict("CANNOT_CHANGE_SELF", "Ask another admin to change your own role or status.");
        var member = await db.Staff.SingleOrDefaultAsync(s => s.Id == id, ct)
                     ?? throw ApiException.NotFound("STAFF_NOT_FOUND", "Staff member not found.");
        if (request.Role is { } role) member.Role = role;
        if (request.Status is { } status) member.Status = status;
        await db.SaveChangesAsync(ct);
        if (member.Status == AccountStatus.Suspended) await tokens.RevokeAllAsync(Audience.Staff, id, ct);
        return TypedResults.Ok(StaffMember.From(member));
    }
}
