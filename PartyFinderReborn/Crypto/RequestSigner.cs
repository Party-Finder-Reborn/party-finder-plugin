using System;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Collections.Generic;
using Org.BouncyCastle.Crypto;
using Org.BouncyCastle.Crypto.Parameters;
using Org.BouncyCastle.OpenSsl;
using Org.BouncyCastle.Security;
using Org.BouncyCastle.Crypto.Signers;

namespace PartyFinderReborn.Crypto
{
    /// <summary>
    /// Client-side request signer using ECDSA P-256 with SHA-256.
    /// Implements the lightweight signing scheme for API requests.
    /// Uses BouncyCastle for WINE compatibility.
    /// </summary>
    public class RequestSigner : IDisposable
    {
        private readonly ECPrivateKeyParameters _privateKey;
        private readonly ISigner _signer;
        private bool _disposed;

        /// <summary>
        /// Initialize the signer with a private key from Constants.
        /// </summary>
        public RequestSigner()
        {
            try
            {
                // Parse PEM key using BouncyCastle
                using var reader = new StringReader(Constants.PrivateKey);
                var pemReader = new PemReader(reader);
                var keyObject = pemReader.ReadObject();
                
                // Extract private key parameters
                _privateKey = keyObject switch
                {
                    ECPrivateKeyParameters ecKey => ecKey,
                    AsymmetricCipherKeyPair keyPair => (ECPrivateKeyParameters)keyPair.Private,
                    _ => throw new ArgumentException($"Unsupported key type: {keyObject?.GetType().Name}")
                };
                
                // Verify it's P-256 (secp256r1)
                var curveName = _privateKey.Parameters.Curve.GetType().Name;
                if (!curveName.Contains("P256") && !curveName.Contains("secp256r1"))
                {
                    // Additional check by key size
                    var keySize = _privateKey.D.BitLength;
                    if (keySize < 250 || keySize > 260) // P-256 keys are ~256 bits
                    {
                        throw new ArgumentException($"Expected P-256 key, got key with {keySize} bits");
                    }
                }
                
                // Initialize ECDSA signer with SHA-256
                _signer = SignerUtilities.GetSigner("SHA256withECDSA");
                _signer.Init(true, _privateKey); // true = signing mode
            }
            catch (Exception ex)
            {
                throw new ArgumentException($"Failed to load private key from Constants: {ex.Message}", ex);
            }
        }

        /// <summary>
        /// Create the canonical string to sign.
        /// Format: {timestamp}\n{method}\n{request-path}\n{sha256(body)}
        /// </summary>
        /// <param name="timestamp">Unix epoch seconds</param>
        /// <param name="method">HTTP method (GET, POST, etc.)</param>
        /// <param name="requestPath">Request path including query parameters</param>
        /// <param name="body">Request body as byte array</param>
        /// <returns>Canonical string ready for signing</returns>
        private static string CreateCanonicalString(long timestamp, string method, string requestPath, byte[] body)
        {
            using var sha256 = SHA256.Create();
            var bodyHash = Convert.ToHexString(sha256.ComputeHash(body)).ToLowerInvariant();
            
            return $"{timestamp}\n{method.ToUpperInvariant()}\n{requestPath}\n{bodyHash}";
        }

        /// <summary>
        /// Sign an HTTP request and return the required signature headers.
        /// Note: This only returns the signature headers. API key authentication
        /// should be handled separately by the existing system.
        /// </summary>
        /// <param name="method">HTTP method (GET, POST, etc.)</param>
        /// <param name="requestPath">Request path including query parameters</param>
        /// <param name="body">Request body (string, byte array, or object)</param>
        /// <returns>Dictionary containing signature headers only</returns>
        public Dictionary<string, string> SignRequest(string method, string requestPath, object? body = null)
        {
            if (_disposed)
                throw new ObjectDisposedException(nameof(RequestSigner));

            // Convert body to bytes
            byte[] bodyBytes = ConvertBodyToBytes(body);
            
            // Generate timestamp
            var timestamp = DateTimeOffset.UtcNow.ToUnixTimeSeconds();
            
            // Create canonical string
            var canonicalString = CreateCanonicalString(timestamp, method, requestPath, bodyBytes);
            
            try
            {
                // Sign the canonical string using BouncyCastle
                var canonicalBytes = Encoding.UTF8.GetBytes(canonicalString);
                _signer.BlockUpdate(canonicalBytes, 0, canonicalBytes.Length);
                var signature = _signer.GenerateSignature();
                
                // Encode signature as base64
                var signatureB64 = Convert.ToBase64String(signature);
                
                // Return only signature headers
                return new Dictionary<string, string>
                {
                    ["X-PFR-Signature"] = signatureB64,
                    ["X-PFR-Timestamp"] = timestamp.ToString()
                };
            }
            catch (Exception ex)
            {
                throw new InvalidOperationException($"Failed to sign request: {ex.Message}", ex);
            }
        }

        /// <summary>
        /// Convert various body types to byte array.
        /// </summary>
        /// <param name="body">Request body</param>
        /// <returns>Body as byte array</returns>
        private static byte[] ConvertBodyToBytes(object? body)
        {
            return body switch
            {
                null => Array.Empty<byte>(),
                string str => Encoding.UTF8.GetBytes(str),
                byte[] bytes => bytes,
                _ => Encoding.UTF8.GetBytes(JsonSerializer.Serialize(body, JsonSerializerOptions.Default))
            };
        }

        public void Dispose()
        {
            if (!_disposed)
            {
                // BouncyCastle objects don't need explicit disposal
                // Just mark as disposed to prevent further use
                _disposed = true;
            }
        }
    }

    /// <summary>
    /// HTTP client wrapper that automatically signs requests and handles API key authentication.
    /// </summary>
    public class SignedHttpClient : IDisposable
    {
        private readonly HttpClient _httpClient;
        private readonly RequestSigner _signer;
        private readonly Configuration _configuration;
        private bool _disposed;

        /// <summary>
        /// Initialize the signed HTTP client.
        /// </summary>
        /// <param name="configuration">Configuration instance to get API key from</param>
        public SignedHttpClient(Configuration configuration)
        {
            _configuration = configuration ?? throw new ArgumentNullException(nameof(configuration));
                
            _httpClient = new HttpClient
            {
                BaseAddress = new Uri(Constants.ApiBaseUrl)
            };
            
            // Add user agent
            _httpClient.DefaultRequestHeaders.UserAgent.ParseAdd(Constants.UserAgent);
            
            _signer = new RequestSigner();
        }

        /// <summary>
        /// Add signature headers and API key to an HTTP request message.
        /// </summary>
        /// <param name="request">HTTP request message</param>
        private async Task AddSignatureHeadersAsync(HttpRequestMessage request)
        {
            if (_disposed)
                throw new ObjectDisposedException(nameof(SignedHttpClient));

            // Add API key if available (dynamically from configuration)
            if (!string.IsNullOrEmpty(_configuration.ApiKey))
            {
                request.Headers.TryAddWithoutValidation("Authorization", $"ApiKey {_configuration.ApiKey}");
            }

            var method = request.Method.Method;
            var path = request.RequestUri?.PathAndQuery ?? "/";
            
            // Get request body
            byte[] body = Array.Empty<byte>();
            if (request.Content != null)
            {
                body = await request.Content.ReadAsByteArrayAsync();
            }
            
            // Sign the request
            var headers = _signer.SignRequest(method, path, body);
            
            // Add signature headers to request
            foreach (var header in headers)
            {
                request.Headers.TryAddWithoutValidation(header.Key, header.Value);
            }
        }

        /// <summary>
        /// Send a GET request with signature.
        /// </summary>
        public async Task<HttpResponseMessage> GetAsync(string requestUri)
        {
            var request = new HttpRequestMessage(HttpMethod.Get, requestUri);
            await AddSignatureHeadersAsync(request);
            return await _httpClient.SendAsync(request);
        }

        /// <summary>
        /// Send a POST request with signature.
        /// </summary>
        public async Task<HttpResponseMessage> PostAsync(string requestUri, HttpContent? content = null)
        {
            var request = new HttpRequestMessage(HttpMethod.Post, requestUri) { Content = content };
            await AddSignatureHeadersAsync(request);
            return await _httpClient.SendAsync(request);
        }

        /// <summary>
        /// Send a PUT request with signature.
        /// </summary>
        public async Task<HttpResponseMessage> PutAsync(string requestUri, HttpContent? content = null)
        {
            var request = new HttpRequestMessage(HttpMethod.Put, requestUri) { Content = content };
            await AddSignatureHeadersAsync(request);
            return await _httpClient.SendAsync(request);
        }

        /// <summary>
        /// Send a DELETE request with signature.
        /// </summary>
        public async Task<HttpResponseMessage> DeleteAsync(string requestUri)
        {
            var request = new HttpRequestMessage(HttpMethod.Delete, requestUri);
            await AddSignatureHeadersAsync(request);
            return await _httpClient.SendAsync(request);
        }

        /// <summary>
        /// Send any HTTP request with signature.
        /// </summary>
        public async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request)
        {
            await AddSignatureHeadersAsync(request);
            return await _httpClient.SendAsync(request);
        }

        public void Dispose()
        {
            if (!_disposed)
            {
                _httpClient?.Dispose();
                _signer?.Dispose();
                _disposed = true;
            }
        }
    }
}
