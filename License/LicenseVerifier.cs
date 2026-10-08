using System;
using System.IO;
using System.Security.Cryptography;
using System.Text;
using System.Web.Script.Serialization;

namespace TranslationEplAddIn.Tool.License
{
    public static class LicenseVerifier
    {
        public static string GetLicensePath()
        {
            try
            {
                string appDataFolder = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "TranslationEplAddIn");
                if (!Directory.Exists(appDataFolder))
                {
                    Directory.CreateDirectory(appDataFolder);
                }
                return Path.Combine(appDataFolder, "license.lic");
            }
            catch
            {
                string pluginFolder = Path.GetDirectoryName(System.Reflection.Assembly.GetExecutingAssembly().Location);
                return Path.Combine(pluginFolder, "license.lic");
            }
        }

        // 默认 RSA-2048 公钥，与发卡工具内置的默认私钥匹配
        public const string DefaultPublicKeyXml = @"<RSAKeyValue><Modulus>6WmuAUbwFwnlJRNuoedojRHBY+h1HxQ1qbd+uof0PToEWaP/N3T8AW3VPhDDbDkXLG1AaNuh0C1d3Z6hKq93bzmUdpRasBYNJp7pm74yMVvaK6n2IU++v9Ho2LrSuAi0QFLcMczLRbz/VuHJ0MoZsYIrLZEv9E6xWTAptK+SxnvkY+qkY+PsOgtalv7hK9UylaujlzOIwjhXXgj5yb/iLLGcSoN4eUhqQhnJ8qdZNlT7YBDm+63mJS/B2V35GGzM8eP+7x9O7GqFvnQ1KzilTBbXuSIgxK5cuVrIrX5L4Ve24dWgei9JhfIbB7M4fFENp2GjYYJs18qFTZ8T64qULQ==</Modulus><Exponent>AQAB</Exponent></RSAKeyValue>";

        public static bool CheckLicense(string licensePath, out LicenseInfo licInfo, out string errorMessage)
        {
            return CheckLicense(licensePath, DefaultPublicKeyXml, out licInfo, out errorMessage);
        }

        public static bool CheckLicense(string licensePath, string publicKeyXml, out LicenseInfo licInfo, out string errorMessage)
        {
            licInfo = null;
            errorMessage = string.Empty;

            if (string.IsNullOrWhiteSpace(publicKeyXml))
            {
                errorMessage = "未配置验签公钥。";
                return false;
            }

            if (!File.Exists(licensePath))
            {
                errorMessage = "未找到授权文件(license.lic)。";
                return false;
            }

            try
            {
                string content = File.ReadAllText(licensePath).Trim();
                string[] parts = content.Split('.');
                if (parts.Length != 2)
                {
                    errorMessage = "许可证格式不正确。";
                    return false;
                }

                string rawJsonBytes = parts[0];
                string signatureBase64 = parts[1];
                
                string jsonData = Encoding.UTF8.GetString(Convert.FromBase64String(rawJsonBytes));
                byte[] signature = Convert.FromBase64String(signatureBase64);

                // 1. 验证签名
                using (var rsa = new RSACryptoServiceProvider())
                {
                    rsa.FromXmlString(publicKeyXml);
                    byte[] dataBytes = Encoding.UTF8.GetBytes(rawJsonBytes);
                    bool isValid = rsa.VerifyData(dataBytes, CryptoConfig.MapNameToOID("SHA256"), signature);
                    if (!isValid)
                    {
                        errorMessage = "授权签名验证失败，许可证已被篡改或不合法！";
                        return false;
                    }
                }

                // 2. 反序列化 (使用 .NET 4.8 自带的 JavaScriptSerializer)
                var serializer = new JavaScriptSerializer();
                licInfo = serializer.Deserialize<LicenseInfo>(jsonData);
                if (licInfo == null)
                {
                    errorMessage = "授权内容无法解析。";
                    return false;
                }

                // 3. 校验机器码
                string localCode = HardwareFingerprint.GetMachineCode();
                if (licInfo.MachineCode != localCode)
                {
                    errorMessage = $"此授权文件绑定的机器码({licInfo.MachineCode})与当前电脑({localCode})不匹配！";
                    return false;
                }

                // 4. 校验到期时间
                if (!DateTime.TryParseExact(licInfo.ExpireDate, "yyyy-MM-dd", null, System.Globalization.DateTimeStyles.None, out DateTime expire))
                {
                    errorMessage = $"授权到期时间格式无效: '{licInfo.ExpireDate}'";
                    return false;
                }

                if (DateTime.Now.Date > expire.Date)
                {
                    errorMessage = $"授权已过期，到期日期为: {licInfo.ExpireDate}";
                    return false;
                }

                return true;
            }
            catch (Exception ex)
            {
                errorMessage = $"授权文件解析失败: {ex.Message}";
                return false;
            }
        }
    }
}
