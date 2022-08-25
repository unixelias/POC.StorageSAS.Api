using Azure;
using Azure.Storage.Blobs;
using Azure.Storage.Blobs.Models;
using Azure.Storage.Sas;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging;
using System;
using System.Collections.Generic;
using System.Net;
using System.Threading.Tasks;

namespace POC.StorageSAS.Api.Controllers
{
    [ApiVersion("1.0")]
    [ApiController]
    [Route("api/v{version:apiVersion}/[controller]")]
    public class StorageController : ControllerBase
    {
        private readonly IConfiguration Configuration;
        private readonly ILogger<StorageController> _logger;
        private readonly BlobServiceClient _blobServiceInternal;
        private readonly BlobServiceClient _blobServiceExternal;
        private static readonly DateTimeOffset DefaultStartsOn = DateTimeOffset.UtcNow.AddMinutes(-15);
        private static readonly DateTimeOffset DefaultEndsOn = DateTimeOffset.UtcNow.AddHours(1);

        public StorageController(ILogger<StorageController> logger, IConfiguration configuration)
        {
            _logger = logger;
            Configuration = configuration;
            _blobServiceExternal = new BlobServiceClient(Configuration["ExternalStorageConnectionString"]);
            _blobServiceInternal = new BlobServiceClient(Configuration["InternalStorageConnectionString"]);
        }

        [HttpGet("file/{newFileName}")]
        [Produces("application/json")]
        [ProducesResponseType(StatusCodes.Status200OK)]
        [ProducesResponseType(StatusCodes.Status400BadRequest)]
        [ProducesResponseType(StatusCodes.Status404NotFound)]
        [ProducesResponseType(StatusCodes.Status500InternalServerError)]
        [ProducesDefaultResponseType]
        public async Task<IActionResult> GetFileUri([FromRoute] string newFileName, [FromQuery] string internalContainerName, [FromQuery] string internalFileName)
        {
            try
            {
                /*
                 * Get file content on internal storage
                 */
                var baseFile = await GetFileFromInternalStorage(internalContainerName, internalFileName);

                /*
                 * Send file to external storage and get read only sas URI
                 */
                Uri readOnlySasUri = await SendFileToExternalStorageAndGetSasUri(newFileName, baseFile);

                _logger.LogInformation($"Successful GET File call: {newFileName}, Uri: {readOnlySasUri}");
                return Ok(readOnlySasUri);
            }
            catch (RequestFailedException ex)
            { 
                return StatusCode((int)ex.Status, ex.Message);
            }
        }

        [HttpDelete("container")]
        [Produces("application/json")]
        [ProducesResponseType(StatusCodes.Status200OK)]
        [ProducesResponseType(StatusCodes.Status404NotFound)]
        [ProducesResponseType(StatusCodes.Status500InternalServerError)]
        [ProducesDefaultResponseType]
        public async Task<IActionResult> DeleteContainetAsync([FromQuery] string containerName)
        {
            /*
             * Delete container
             */
            try
            {
                await _blobServiceExternal.DeleteBlobContainerAsync(containerName);

                return Ok();
            }
            catch (RequestFailedException ex)
            {
                return StatusCode((int)ex.Status, ex.Message);
            }
            catch (Exception ex)
            {
                return StatusCode((int)HttpStatusCode.InternalServerError, ex.Message);
            }
        }

        private async Task<Uri> SendFileToExternalStorageAndGetSasUri(string newFileName, byte[] blobContent)
        {
            /* You can set container name to something that identifies
             * who an when is getting the file, for excample
            */
            string containerName = $"sas-container-{DateTime.UtcNow.Ticks}";
            const string policyPrefix = "access-policy-";
            string storeAccessPolicyOwner = policyPrefix + "owner";
            string storeAccessPolicyReadOnly = policyPrefix + "read-only";

            /*
             * Get new conteiner name and create it if nor exists
             */
            BlobContainerClient externalContainer = _blobServiceExternal.GetBlobContainerClient(containerName);
            try
            {
                externalContainer.CreateIfNotExists();
            }
            catch (RequestFailedException ex)
            {
                _logger.LogError("Create or get container falied" + ex.Message);
                throw new Exception(ex.Message, ex);
            }

            try
            {
                /*
                 * Create read/write Sas URI to send the file to external storage
                 * only valid for 1 minute
                 */
                await SetStoreAccessPolicy(externalContainer, storeAccessPolicyOwner, "racwdl");
                Uri rwSasUri = GetBlobSasUri(externalContainer, newFileName, BlobSasPermissions.Create | BlobSasPermissions.Write | BlobSasPermissions.Read, DateTimeOffset.UtcNow.AddMinutes(1), null);
                BlobClient blob = new(rwSasUri);

                /*
                 * Create operation: Upload a blob with the specified name to the container.
                 * If the blob does not exist, it will be created. If it does exist, it will be overwritten.
                 */
                blob.Upload(BinaryData.FromBytes(blobContent));
                _logger.LogInformation("Create operation succeeded for SAS " + rwSasUri);
            }
            catch (RequestFailedException ex)
            {
                _logger.LogError("Create file operation failed " + ex.Message);
                throw new Exception(ex.Message, ex);
            }

            /*
             * Change access policy on new container to read only permissions
             * Then generate a new Sas key for blob file also with read only permission
             * "r" means read only
             */

            await SetStoreAccessPolicy(externalContainer, storeAccessPolicyReadOnly, "r");
            Uri readOnlySasUri = GetBlobSasUri(externalContainer, newFileName, BlobSasPermissions.Read, DateTimeOffset.UtcNow.AddHours(24), null);

            /*
             * With this link we have direct, but restricted access to the file
             */

            return readOnlySasUri;
        }

        private async Task<byte[]> GetFileFromInternalStorage(string internalContainerName, string fileName)
        {
            /*
             * Find file on internal storage and return content as byte[]
             */
            BlobContainerClient internalContainer = _blobServiceInternal.GetBlobContainerClient(internalContainerName);

            BlobClient blobFile = internalContainer.GetBlobClient(fileName);
            try
            {
                var content = await blobFile.DownloadContentAsync();
                var contentBytes = content.Value.Content.ToArray();
                return contentBytes;
            }
            catch (Exception ex)
            {
                _logger.LogError("Error getting blob file from internal storage" + ex.Message);             
                throw ex;
            }
        }

        private static Uri GetBlobSasUri(BlobContainerClient container, string blobName, BlobSasPermissions permissions, DateTimeOffset? expiresOn, DateTimeOffset? startsOn)
        {
            /*
             * Get BlobClient and check if can generate User Sas Key
             * For more details check the link: https://docs.microsoft.com/en-us/rest/api/storageservices/create-service-sas
             */
            BlobClient blobClient = container.GetBlobClient(blobName);

            if (!blobClient.CanGenerateSasUri)
            {
                throw new RequestFailedException("Error getting SAS Uri!");
            }

            /*
             * We set allowed IP addresses for constructing a Shared Access Signature
             */
            var ipStart = new IPAddress(01);
            var ipRange = new SasIPRange(ipStart);

            BlobSasBuilder policy = new()
            {
                BlobContainerName = container.Name,
                BlobName = blobName,
                Resource = "b",
                StartsOn = startsOn ?? DefaultStartsOn,
                ExpiresOn = expiresOn ?? DefaultEndsOn,
                IPRange = ipRange
            };

            /*
             * Set custom input permissions in the policy to get the key with them
             */
            //policy;
            policy.SetPermissions(permissions);
            Uri sasUri = blobClient.GenerateSasUri(policy);
            return sasUri;
        }

        private static async Task SetStoreAccessPolicy(BlobContainerClient container, string policyName, string rawPermissions)
        {
            /*
             * RawPermissions means type of acces, like "rcw" [read, create, write]  for more details look this link:
             * https://docs.microsoft.com/en-us/rest/api/storageservices/create-service-sas#permissions-for-a-directory-container-or-blob
             */
            IEnumerable<BlobSignedIdentifier> permissionsList = new[]
            {
                new BlobSignedIdentifier
                {
                    Id = policyName,
                    AccessPolicy =
                        new BlobAccessPolicy
                        {
                            PolicyStartsOn = DefaultStartsOn,
                            PolicyExpiresOn =  DefaultEndsOn,
                            Permissions = rawPermissions
                        }
                }
            };
            await container.SetAccessPolicyAsync(PublicAccessType.None, permissionsList);
        }
    }
}