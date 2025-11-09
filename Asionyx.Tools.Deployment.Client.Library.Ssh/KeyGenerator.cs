using Org.BouncyCastle.Crypto.Generators;
using Org.BouncyCastle.Security;
using Org.BouncyCastle.OpenSsl;
using Org.BouncyCastle.Crypto.Parameters;

namespace Asionyx.Tools.Deployment.Client.Library.Ssh
{
    public static class KeyGenerator
    {
        /// <summary>
        /// Generates an RSA keypair and returns (privatePem, publicOpenSsh)
        /// </summary>
        public static (string PrivatePem, string PublicOpenSsh) GenerateRsaKeyPair(int bits = 2048)
        {
            var gen = new RsaKeyPairGenerator();
            gen.Init(new Org.BouncyCastle.Crypto.KeyGenerationParameters(new SecureRandom(), bits));
            var pair = gen.GenerateKeyPair();

            // private PEM
            string privatePem;
            {
                var rsaPriv = (RsaPrivateCrtKeyParameters)pair.Private;
                using var swp = new StringWriter();
                var pemWriter = new PemWriter(swp);
                pemWriter.WriteObject(rsaPriv);
                pemWriter.Writer.Flush();
                privatePem = swp.ToString();
            }

            // public OpenSSH
            var rsaPub = (RsaKeyParameters)pair.Public;
            byte[] e = rsaPub.Exponent.ToByteArrayUnsigned();
            byte[] n = rsaPub.Modulus.ToByteArrayUnsigned();

            byte[] sshRsa;
            using (var ms = new MemoryStream())
            {
                void WriteUInt32(uint v)
                {
                    var b = new byte[4];
                    b[0] = (byte)((v >> 24) & 0xff);
                    b[1] = (byte)((v >> 16) & 0xff);
                    b[2] = (byte)((v >> 8) & 0xff);
                    b[3] = (byte)(v & 0xff);
                    ms.Write(b, 0, 4);
                }
                void WriteMpint(byte[] arr)
                {
                    if (arr.Length == 0) { WriteUInt32(0); return; }
                    if ((arr[0] & 0x80) != 0)
                    {
                        var withZero = new byte[arr.Length + 1];
                        withZero[0] = 0x00;
                        Buffer.BlockCopy(arr, 0, withZero, 1, arr.Length);
                        WriteUInt32((uint)withZero.Length);
                        ms.Write(withZero, 0, withZero.Length);
                    }
                    else
                    {
                        WriteUInt32((uint)arr.Length);
                        ms.Write(arr, 0, arr.Length);
                    }
                }
                var name = System.Text.Encoding.ASCII.GetBytes("ssh-rsa");
                WriteUInt32((uint)name.Length);
                ms.Write(name, 0, name.Length);
                WriteMpint(e);
                WriteMpint(n);
                sshRsa = ms.ToArray();
            }
            var pub64 = Convert.ToBase64String(sshRsa);
            var pubKey = $"ssh-rsa {pub64} generated-key";

            return (privatePem, pubKey + "\n");
        }

        public static void WriteKeyPairFiles(string prefix, string privatePem, string publicOpenSsh)
        {
            File.WriteAllText(prefix, privatePem);
            File.WriteAllText(prefix + ".pub", publicOpenSsh);
        }
    }
}
