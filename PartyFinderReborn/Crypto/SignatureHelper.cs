using System;
using System.Collections.Generic;

namespace PartyFinderReborn.Crypto
{
    /// <summary>
    /// Helper class to add ECDSA signatures to existing HTTP requests.
    /// This allows integrating with existing HTTP client implementations.
    /// </summary>
    public static class SignatureHelper
    {
        private static readonly Lazy<RequestSigner> _signer = new(() => new RequestSigner());
        
        /// <summary>
        /// Get signature headers for an HTTP request.
        /// These headers should be added to your existing request alongside
        /// the Authorization header and other existing headers.
        /// </summary>
        /// <param name="method">HTTP method (GET, POST, etc.)</param>
        /// <param name="requestPath">Request path including query parameters</param>
        /// <param name="body">Request body (optional)</param>
        /// <returns>Dictionary containing X-PFR-Signature and X-PFR-Timestamp headers</returns>
        public static Dictionary<string, string> GetSignatureHeaders(string method, string requestPath, object? body = null)
        {
            return _signer.Value.SignRequest(method, requestPath, body);
        }
        
        /// <summary>
        /// Add signature headers to an existing HttpRequestMessage.
        /// This method modifies the request in-place by adding the signature headers.
        /// </summary>
        /// <param name="request">The HTTP request to sign</param>
        /// <param name="body">Request body (if different from request.Content)</param>
        public static async Task AddSignatureHeadersAsync(HttpRequestMessage request, object? body = null)
        {
            var method = request.Method.Method;
            var path = request.RequestUri?.PathAndQuery ?? "/";
            
            // Use provided body or read from request content
            object? requestBody = body;
            if (requestBody == null && request.Content != null)
            {
                requestBody = await request.Content.ReadAsByteArrayAsync();
            }
            
            var signatureHeaders = GetSignatureHeaders(method, path, requestBody);
            
            foreach (var header in signatureHeaders)
            {
                request.Headers.TryAddWithoutValidation(header.Key, header.Value);
            }
        }
        
        /// <summary>
        /// Create a dictionary of all headers needed for a signed request.
        /// This includes the signature headers that should be added to your existing
        /// Authorization and other headers.
        /// </summary>
        /// <param name="method">HTTP method</param>
        /// <param name="requestPath">Request path including query parameters</param>
        /// <param name="body">Request body</param>
        /// <param name="existingHeaders">Existing headers to merge with (optional)</param>
        /// <returns>Combined headers including signatures</returns>
        public static Dictionary<string, string> CreateSignedHeaders(string method, string requestPath, 
            object? body = null, Dictionary<string, string>? existingHeaders = null)
        {
            var signatureHeaders = GetSignatureHeaders(method, requestPath, body);
            
            if (existingHeaders == null)
                return signatureHeaders;
            
            var combinedHeaders = new Dictionary<string, string>(existingHeaders);
            foreach (var header in signatureHeaders)
            {
                combinedHeaders[header.Key] = header.Value;
            }
            
            return combinedHeaders;
        }
    }
}
