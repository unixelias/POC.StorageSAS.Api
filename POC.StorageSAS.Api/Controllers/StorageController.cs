using Azure;
using Azure.Storage.Blobs;
using Azure.Storage.Blobs.Models;
using Azure.Storage.Sas;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging;
using System;
using System.Collections.Generic;
using System.Net;
using System.Threading.Tasks;

namespace POC.StorageSAS.Api.Controllers
{
    [ApiController]
    [Route("[controller]")]
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
            _blobServiceExternal = new BlobServiceClient(Configuration["InternalStorageConnectionString"]);
            _blobServiceInternal = new BlobServiceClient(Configuration["ExternalStorageConnectionString"]);
        }

        [HttpGet]
        public async Task<IActionResult> GetFileUri([FromQuery] string blobName, [FromQuery] string internalContainerName, [FromQuery] string internalFileName)
        {
            /*
             * Get file content on internal storage
             */
            BlobContainerClient internalContainer = _blobServiceInternal.GetBlobContainerClient(internalContainerName);
            var baseFile = await GetFileFromInternalStorage(internalContainer, internalFileName);


            /*
             * Send file to external storage and get read only sas URI
             */
            Uri readOnlySasUri = await SendFileToExternalStorageAndGetUri(blobName, baseFile);

            _logger.LogInformation($"Successful GET File call: {blobName}, Uri: {readOnlySasUri}");
            return Ok(readOnlySasUri);
        }

        private async Task<Uri> SendFileToExternalStorageAndGetUri(string blobName, byte[] blobContent)
        {
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
                throw new Exception(ex.Message, ex);
            }


            /*
             * Create read/write Sas URI to send the file to external storage 
             */
            await CreateStoreAccessPolicy(externalContainer, storeAccessPolicyOwner, "racwdl");
            Uri rwSasUri = GetBlobSasUri(externalContainer, blobName, BlobSasPermissions.Create | BlobSasPermissions.Write | BlobSasPermissions.Read, DateTimeOffset.UtcNow.AddMinutes(15), null);
            BlobClient blob = new(rwSasUri);


            /* 
             * Create operation: Upload a blob with the specified name to the container.
             * If the blob does not exist, it will be created. If it does exist, it will be overwritten.
             */
            try
            {
                blob.Upload(BinaryData.FromBytes(blobContent));
                _logger.LogInformation("Create operation succeeded for SAS " + rwSasUri);
            }
            catch (RequestFailedException e)
            {
                _logger.LogInformation("Create operation failed for SAS " + rwSasUri);
                _logger.LogInformation("Additional error information: " + e.Message);
            }

            /*
             * Change access policy on new container read only permissions
             * Then generate a new Sas key for blob file also with read only permission
             * "r" means read only
             */

            await CreateStoreAccessPolicy(externalContainer, storeAccessPolicyReadOnly, "r");
            Uri readOnlySasUri = GetBlobSasUri(externalContainer, blobName, BlobSasPermissions.Read, DateTimeOffset.UtcNow.AddMinutes(5), null);

            /*
             * With this link we have restricted access to storage
             */

            return readOnlySasUri;
        }

        public static async Task<byte[]> GetFileFromInternalStorage(BlobContainerClient container, string fileName)
        {
            /*
             * Find file on internal storage and return content as byte[]
             */
            BlobClient blobFile = container.GetBlobClient(fileName);
            try
            {
                var content = await blobFile.DownloadContentAsync();
                var contentBytes = content.Value.Content.ToArray();
                return contentBytes;
            }
            catch (Exception ex)
            {
                throw new Exception("Error getting blob file from internal storage", ex);
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

        private static async Task CreateStoreAccessPolicy(BlobContainerClient container, string policyName, string rawPermissions)
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