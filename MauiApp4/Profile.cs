using Supabase.Postgrest.Attributes;
using Supabase.Postgrest.Models;
using System;

[Table("profiles")] // Must match table name in Supabase
public class Profile : BaseModel
{
    [PrimaryKey("id")]
    public string Id { get; set; }

    [Column("username")]
    public string Username { get; set; }

    [Column("lastname")]
    public string LastName { get; set; }

    [Column("email")]
    public string Email { get; set; }

    [Column("created_at")]
    public DateTime CreatedAt { get; set; }

    [Column("updated_at")]
    public DateTime UpdatedAt { get; set; }

    [Column("phone_number")]
    public string PhoneNumber { get; set; }

    [Column("profile_picture_url")]
    public string ProfilePictureUrl { get; set; }

    [Column("bio")]
    public string Bio { get; set; }
}