namespace Structly.AI.Tests;

public sealed class ImagePartTests
{
    [Theory]
    [InlineData("89504E470D0A1A0A", "image/png")]
    [InlineData("FFD8FF", "image/jpeg")]
    [InlineData("474946383761", "image/gif")]
    [InlineData("474946383961", "image/gif")]
    [InlineData("524946460000000057454250", "image/webp")]
    public void ByteSignaturesProduceCanonicalDataUrls(string hex, string mediaType)
    {
        var bytes = Convert.FromHexString(hex);
        var image = ImagePart.FromBytes(bytes, mediaType.ToUpperInvariant(), ImageDetail.High);
        Assert.Equal($"data:{mediaType};base64,{Convert.ToBase64String(bytes)}", image.Url);
        Assert.Equal(ImageDetail.High, image.Detail);
        Assert.Equal(image.Url, ImagePart.FromBytes(bytes).Url);
        bytes[0] = 0;
        Assert.Throws<ArgumentException>(() => ImagePart.FromBytes(bytes));
    }

    [Fact]
    public void UnsupportedTruncatedOrMismatchedInputsAreRejected()
    {
        Assert.Throws<ArgumentException>(() => ImagePart.FromBytes([]));
        Assert.Throws<ArgumentException>(() => ImagePart.FromBytes("<svg/>"u8));
        Assert.Throws<ArgumentException>(() => ImagePart.FromBytes(Convert.FromHexString("89504E47")));
        Assert.Throws<ArgumentException>(() => ImagePart.FromBytes("GIF89a"u8, "image/png"));
        Assert.Throws<ArgumentOutOfRangeException>(() => ImagePart.FromBytes("GIF89a"u8, detail: (ImageDetail)42));
    }
}
