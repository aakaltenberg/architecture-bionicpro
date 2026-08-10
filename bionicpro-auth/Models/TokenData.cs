namespace bionicpro_auth.Models
{
    public class TokenData
    {
        public string AccessToken { get; set; }
        public string RefreshToken { get; set; }
        public DateTime AccessTokenExpiry { get; set; }
    }
}