using System;
using System.Collections.Generic;

namespace APCS.Domain.Entities;

public partial class ShareHashtag
{
    public Guid Id { get; set; }

    public Guid SocialMediaShareId { get; set; }

    public string Hashtag { get; set; } = null!;

    public int Position { get; set; }

    public virtual SocialMediaShare SocialMediaShare { get; set; } = null!;
}
