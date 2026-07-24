using System.ComponentModel.DataAnnotations;

namespace CloudNews.Functions.Models;

public class Article
{
    public int Id { get; set; }

    [Required, MaxLength(500)]
    public string Title { get; set; } = string.Empty;

    [Required, MaxLength(600)]
    public string Slug { get; set; } = string.Empty;

    [Required]
    public string Content { get; set; } = string.Empty;

    [MaxLength(1000)]
    public string? ThumbnailUrl { get; set; }

    public int CategoryId { get; set; }
    public int AuthorId   { get; set; }

    public bool      IsPublished { get; set; } = false;
    public DateTime? PublishedAt { get; set; }
    public int       Views       { get; set; } = 0;

    // ── Approval workflow ──────────────────────────────────────────────────
    // NotRequired — article by SuperAdmin/Admin/Reporter (goes live immediately)
    // Pending     — article submitted by Employee, waiting SuperAdmin review
    // Approved    — SuperAdmin approved, now IsPublished = true and goes live
    // Rejected    — SuperAdmin rejected, employee sees ApprovalNote reason
    [MaxLength(20)]
    public string ApprovalStatus { get; set; } = "NotRequired";

    // Reason shown to employee when article is rejected
    [MaxLength(500)]
    public string? ApprovalNote { get; set; }

    // Who approved or rejected
    public int?      ApprovedById { get; set; }
    public DateTime? ApprovedAt   { get; set; }

    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;
    public DateTime UpdatedAt { get; set; } = DateTime.UtcNow;

    // Navigation
    public Category? Category   { get; set; }
    public User?     Author     { get; set; }
    public User?     ApprovedBy { get; set; }
}

// ── Approval status constants ─────────────────────────────────────────────────
public static class ApprovalStatus
{
    public const string NotRequired = "NotRequired";  // SuperAdmin/Admin articles
    public const string Pending     = "Pending";      // Employee submitted, awaiting review
    public const string Approved    = "Approved";     // Approved by SuperAdmin → goes live
    public const string Rejected    = "Rejected";     // Rejected → employee sees reason
}
