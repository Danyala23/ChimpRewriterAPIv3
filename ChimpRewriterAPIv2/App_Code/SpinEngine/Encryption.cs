using System;
using System.Diagnostics;
using System.Reflection;
using System.Text;
using java.io;
using java.security.spec;
using javax.crypto;
using javax.crypto.spec;

namespace ChimpRewriterAPIv3.SpinEngine
{
    [Obfuscation(Feature = "encryptmethod", Exclude = false)]
    internal static class Encryption
    {
        private static readonly byte[] _iv = { 0xB2, 0x12, 0xD5, 0xB2, 0x44, 0x21, 0xC3, 0xC3 };
        private static readonly byte[] _keyPass = Encoding.ASCII.GetBytes("gr8ch!mp");

        #region Encrypt/Decrypt

        /// <summary>
        ///     Encrypt a file.
        /// </summary>
        /// <param name="inFile">Source filename</param>
        /// <param name="outFile">Encrypted output filename</param>
        [Obfuscation(Feature = "encryptmethod", Exclude = false)]
        internal static void Encrypt(string inFile, string outFile)
        {
            try
            {
                Cipher ecipher = Cipher.getInstance("DES/CBC/PKCS5Padding");
                SecretKey key = new SecretKeySpec(_keyPass, KeyGenerator.getInstance("DES").getAlgorithm());
                AlgorithmParameterSpec paramSpec = new IvParameterSpec(_iv);
                ecipher.init(Cipher.ENCRYPT_MODE, key, paramSpec);

                var buf = new byte[1024];
                using (var input = new FileInputStream(inFile))
                {
                    using (var outputStream = new FileOutputStream(outFile))
                    {
                        using (var output = new CipherOutputStream(outputStream, ecipher))
                        {
                            int numRead;
                            while ((numRead = input.read(buf)) >= 0) output.write(buf, 0, numRead);
                            output.close();
                        }
                        outputStream.close();
                    }
                    input.close();
                }
            }
            catch (Exception ex)
            {
                Debug.WriteLine("!!! Encryption.Encrypt: " + ex.Message);
            }
        }

        /// <summary>
        ///     Decrypt an encrypted cypher stream.
        /// </summary>
        /// <param name="inFile">Source filename</param>
        /// <returns>A decrypted file stream</returns>
        [Obfuscation(Feature = "encryptmethod", Exclude = false)]
        internal static CipherInputStream DecryptStream(string inFile)
        {
            try
            {
                Cipher dcipher = Cipher.getInstance("DES/CBC/PKCS5Padding");
                SecretKey key = new SecretKeySpec(_keyPass, KeyGenerator.getInstance("DES").getAlgorithm());
                AlgorithmParameterSpec paramSpec = new IvParameterSpec(_iv);
                dcipher.init(Cipher.DECRYPT_MODE, key, paramSpec);
                var cis = new CipherInputStream(new FileInputStream(inFile), dcipher);
                return cis;
            }
            catch (Exception ex)
            {
                Debug.WriteLine("!!! Encryption.DecryptStream: " + ex.Message);
                return null;
            }
        }

        /// <summary>
        ///     Decrypts a text file.
        /// </summary>
        /// <param name="inFile">Source filename</param>
        [Obfuscation(Feature = "encryptmethod", Exclude = false)]
        internal static string Decrypt(string inFile)
        {
            try
            {
                // read file
                var input = new FileInputStream(inFile);
                var encText = new byte[input.available()];
                input.read(encText);
                input.close();

                //Decrypt
                Cipher dcipher = Cipher.getInstance("DES/CBC/PKCS5Padding");
                SecretKey key = new SecretKeySpec(_keyPass, KeyGenerator.getInstance("DES").getAlgorithm());
                AlgorithmParameterSpec paramSpec = new IvParameterSpec(_iv);
                dcipher.init(Cipher.DECRYPT_MODE, key, paramSpec);
                byte[] decryptedText = dcipher.doFinal(encText);
                return Encoding.Default.GetString(decryptedText);
            }
            catch (Exception ex)
            {
                Debug.WriteLine("!!! Encryption.Decrypt: " + ex.Message);
            }
            return null;
        }

        #endregion
    }
}