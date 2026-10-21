using NetTopologySuite.Geometries;
using Elastic.Clients.Elasticsearch;
using Elastic.Clients.Elasticsearch.Aggregations;
using Elastic.Clients.Elasticsearch.QueryDsl;
using Habitera.DTOs;
using Habitera.Models;
using Habitera.Repositories;
using Microsoft.EntityFrameworkCore;
using ElasticBoundingBox = Elastic.Clients.Elasticsearch.TopLeftBottomRightGeoBounds;
using ElasticGeoLocation = Elastic.Clients.Elasticsearch.GeoLocation;

namespace Habitera.Services
{

    public class DatabaseSearchService
    {
        private readonly IUnitOfWork _unitOfWork;
        private readonly ILogger<DatabaseSearchService> _logger;

        public DatabaseSearchService(
            IUnitOfWork unitOfWork,
            ILogger<DatabaseSearchService> logger)
        {
            _unitOfWork = unitOfWork;
            _logger = logger;
        }

        public async Task<PropertySearchResponse> SearchPropertiesAsync(PropertySearchRequest request)
        {
            var properties = await _unitOfWork.Properties.GetAllPublishedAsync(PropertyStatus.Active);
            var query = properties.AsQueryable();

            // Text search on title and description
            if (!string.IsNullOrWhiteSpace(request.Query))
            {
                var searchTerm = request.Query.ToLower();
                query = query.Where(p =>
                    p.Title.ToLower().Contains(searchTerm) ||
                    p.Description.ToLower().Contains(searchTerm) ||
                    p.City.ToLower().Contains(searchTerm)
                );
            }

            // Location filters
            if (!string.IsNullOrWhiteSpace(request.City))
                query = query.Where(p => p.City == request.City);

            if (!string.IsNullOrWhiteSpace(request.State))
                query = query.Where(p => p.State == request.State);

            // Price filters
            if (request.MinPrice.HasValue)
                query = query.Where(p => p.Price >= request.MinPrice.Value);

            if (request.MaxPrice.HasValue)
                query = query.Where(p => p.Price <= request.MaxPrice.Value);

            // Bedroom filters
            if (request.MinBedrooms.HasValue)
                query = query.Where(p => p.Bedrooms >= request.MinBedrooms.Value);

            if (request.MaxBedrooms.HasValue)
                query = query.Where(p => p.Bedrooms <= request.MaxBedrooms.Value);

            // Bathroom filters
            if (request.MinBathrooms.HasValue)
                query = query.Where(p => p.Bathrooms >= request.MinBathrooms.Value);

            // Property type filter
            if (request.PropertyTypes?.Any() == true)
            {
                var types = request.PropertyTypes
                    .Select(t => Enum.Parse<PropertyType>(t))
                    .ToList();
                query = query.Where(p => types.Contains(p.PropertyType));
            }

            // Listing type filter
            if (request.ListingTypes?.Any() == true)
            {
                var types = request.ListingTypes
                    .Select(t => Enum.Parse<ListingType>(t))
                    .ToList();
                query = query.Where(p => types.Contains(p.ListingType));
            }

            // Geospatial search
            if (request.GeoSearch != null)
            {
                if (request.GeoSearch.Latitude.HasValue &&
                    request.GeoSearch.Longitude.HasValue &&
                    request.GeoSearch.RadiusKm.HasValue)
                {
                    var radiusInMeters = request.GeoSearch.RadiusKm.Value * 1000;
                    var searchPoint = new Point(
                        (double)request.GeoSearch.Longitude.Value,
                        (double)request.GeoSearch.Latitude.Value
                    )
                    { SRID = 4326 };

                    query = query.Where(p =>
                        p.Latitude != 0 && p.Longitude != 0
                    );

                    // Filter by approximate bounding box first (faster)
                    var latDelta = request.GeoSearch.RadiusKm.Value / 111.0;
                    var lonDelta = request.GeoSearch.RadiusKm.Value /
                        (111.0 * Math.Cos(
                            (double)request.GeoSearch.Latitude.Value * Math.PI / 180.0));

                    query = query.Where(p =>
                        p.Latitude >= (decimal)((double)request.GeoSearch.Latitude.Value - latDelta) &&
                        p.Latitude <= (decimal)((double)request.GeoSearch.Latitude.Value + latDelta) &&
                        p.Longitude >= (decimal)((double)request.GeoSearch.Longitude.Value - lonDelta) &&
                        p.Longitude <= (decimal)((double)request.GeoSearch.Longitude.Value + lonDelta)
                    );
                }
            }

            // Get total count before pagination
            var totalCount = query.Count();

            // Sorting
            query = request.SortBy?.ToLower() switch
            {
                "price" => request.SortOrder?.ToLower() == "desc"
                    ? query.OrderByDescending(p => p.Price)
                    : query.OrderBy(p => p.Price),
                "newest" => query.OrderByDescending(p => p.CreatedAt),
                "popular" => query.OrderByDescending(p => p.ViewCount),
                _ => query.OrderByDescending(p => p.CreatedAt)
            };

            // Pagination
            var pagedProperties = query
                .Skip((request.PageNumber - 1) * request.PageSize)
                .Take(request.PageSize)
                .ToList();

            // Eager load images for each property
            foreach (var property in pagedProperties)
            {
                if (property.Images != null)
                {
                    property.Images = property.Images.OrderBy(i => i.DisplayOrder).Take(1).ToList();
                }
            }

            var propertyDtos = pagedProperties.Select(p => MapToDTO(p)).ToList();

            return new PropertySearchResponse
            {
                Properties = propertyDtos,
                TotalCount = totalCount,
                PageNumber = request.PageNumber,
                PageSize = request.PageSize,
                TotalPages = (int)Math.Ceiling(totalCount / (double)request.PageSize)
            };
        }

        public async Task<List<PropertyDTO>> SearchByRadiusAsync(
            decimal latitude,
            decimal longitude,
            double radiusKm)
        {
            // Using Haversine formula approximation
            var properties = await _unitOfWork.Properties.GetAllAsync();

            var nearby = properties
                .Where(p => p.IsPublished && p.Status == PropertyStatus.Active)
                .Select(p => new
                {
                    Property = p,
                    Distance = CalculateDistance(latitude, longitude, p.Latitude, p.Longitude)
                })
                .Where(x => x.Distance <= radiusKm)
                .OrderBy(x => x.Distance)
                .Take(50)
                .Select(x => MapToDTO(x.Property))
                .ToList();

            return nearby;
        }

        private double CalculateDistance(decimal lat1, decimal lon1, decimal lat2, decimal lon2)
        {
            const double earthRadiusKm = 6371.0;
            var dLat = DegreesToRadians((double)(lat2 - lat1));
            var dLon = DegreesToRadians((double)(lon2 - lon1));

            var a = Math.Sin(dLat / 2) * Math.Sin(dLat / 2) +
                    Math.Cos(DegreesToRadians((double)lat1)) *
                    Math.Cos(DegreesToRadians((double)lat2)) *
                    Math.Sin(dLon / 2) * Math.Sin(dLon / 2);

            var c = 2 * Math.Atan2(Math.Sqrt(a), Math.Sqrt(1 - a));
            return earthRadiusKm * c;
        }

        private double DegreesToRadians(double degrees) => degrees * Math.PI / 180.0;

        private PropertyDTO MapToDTO(Property property)
        {
            var primaryImage = property.Images?.FirstOrDefault(i => i.IsPrimary);
            var fullAddress = string.Join(", ", new[]
            {
                property.Street, property.City, property.State,
                property.PostalCode, property.Country
            }.Where(s => !string.IsNullOrWhiteSpace(s)));

            return new PropertyDTO
            {
                Id = property.Id,
                AgentId = property.AgentId,
                Title = property.Title,
                Description = property.Description,
                PropertyType = property.PropertyType,
                ListingType = property.ListingType,
                Tenor = property.Tenor,
                Street = property.Street,
                City = property.City,
                State = property.State,
                Country = property.Country,
                PostalCode = property.PostalCode,
                FullAddress = fullAddress,
                Latitude = property.Latitude,
                Longitude = property.Longitude,
                Bedrooms = property.Bedrooms,
                Bathrooms = property.Bathrooms,
                SquareFeet = property.SquareFeet,
                LotSize = property.LotSize,
                YearBuilt = property.YearBuilt,
                Price = property.Price,
                Currency = property.Currency,
                Status = property.Status.ToString(),
                IsPublished = property.IsPublished,
                IsFeatured = property.IsFeatured,
                ViewCount = property.ViewCount,
                FavoriteCount = property.FavoriteCount,
                Images = property.Images?.Select(i => new PropertyImageDTO
                {
                    Id = i.Id,
                    ImageUrl = i.ImageUrl,
                    ThumbnailUrl = i.ThumbnailUrl,
                    DisplayOrder = i.DisplayOrder,
                    IsPrimary = i.IsPrimary
                }).ToList() ?? new List<PropertyImageDTO>(),
                PrimaryImageUrl = primaryImage?.ImageUrl ?? property.Images?.OrderBy(i => i.DisplayOrder).FirstOrDefault()?.ImageUrl,
                CreatedAt = property.CreatedAt,
                UpdatedAt = property.UpdatedAt,
                PublishedAt = property.PublishedAt,
                PricePerSquareFoot = property.SquareFeet > 0
                    ? (double)(property.Price / property.SquareFeet)
                    : 0,
                DaysOnMarket = property.PublishedAt.HasValue
                    ? (DateTime.UtcNow - property.PublishedAt.Value).Days
                    : 0
            };
        }
    }

    public interface IElasticsearchService
    {
        Task<bool> IndexExistsAsync();
        Task<bool> CreateIndexAsync();
        Task<bool> IndexPropertyAsync(Property property);
        Task<PropertySearchResponse> SearchPropertiesAsync(PropertySearchRequest request);
        Task<bool> DeleteIndexAsync();
        Task<bool> UpdatePropertyAsync(Guid propertyId, Property property);
        Task<bool> DeletePropertyAsync(Guid propertyId);
        Task<bool> UpdatePropertyStatusAsync(Guid propertyId, PropertyStatus status);
        Task<bool> IncrementViewCountAsync(Guid propertyId);
        Task<bool> BulkIndexPropertiesAsync(IEnumerable<Property> properties);
        Task<bool> ReindexAllPropertiesAsync();
        Task<List<string>> AutocompleteAsync(string query, int limit = 10);
        Task<bool> UpdateFavoriteCountAsync(Guid propertyId, int count);
        Task<List<PropertyDTO>> SearchByRadiusAsync(double latitude, double longitude, double radiusKm, int limit = 50);
        Task<List<PropertyDTO>> SearchByBoundingBoxAsync(double topLeftLat, double topLeftLon, double bottomRightLat, double bottomRightLon, int limit = 100);
        Task<List<PropertyDTO>> GetSimilarPropertiesAsync(Guid propertyId, int limit = 10);
        Task<List<PropertyDTO>> GetRecommendedPropertiesAsync(string userId, int limit = 10);
        Task<List<string>> GetLocationSuggestionsAsync(string query, int limit = 10);
    }

    public class ElasticsearchService : IElasticsearchService
    {
        private readonly ElasticsearchClient _client;
        private readonly IUnitOfWork _unitOfWork;
        private readonly ILogger<ElasticsearchService> _logger;
        private const string IndexName = "properties";

        public ElasticsearchService(
            ElasticsearchClient client,
            IUnitOfWork unitOfWork,
            ILogger<ElasticsearchService> logger)
        {
            _client = client;
            _unitOfWork = unitOfWork;
            _logger = logger;
        }

        public async Task<bool> IndexExistsAsync()
        {
            var response = await _client.Indices.ExistsAsync(IndexName);
            return response.Exists;
        }

        public async Task<bool> CreateIndexAsync()
        {
            var response = await _client.Indices.CreateAsync(IndexName, c => c
                .Mappings(m => m
                    .Properties<PropertyDTO>(p => p
                        .Text(t => t.Title, td => td
                            .Analyzer("standard")
                            .Fields(f => f
                                .Text("autocomplete", ac => ac
                                    .Analyzer("autocomplete")
                                    .SearchAnalyzer("autocomplete_search")
                                )
                            )
                        )
                    .Text(t => t.Description, td => td.Analyzer("standard"))
                            .Text(t => t.FullAddress, td => td.Analyzer("standard"))

                            .Keyword(t => t.AgentId)
                            .Keyword(t => t.PropertyType)
                            .Keyword(t => t.ListingType)
                            .Keyword(t => t.City)
                            .Keyword(t => t.State)
                            .Keyword(t => t.Country)
                            .Keyword(t => t.Status)
                            .Keyword(t => t.Currency)

                            .GeoPoint(t => t.Location)

                            .IntegerNumber(t => t.Bedrooms)
                            .IntegerNumber(t => t.ViewCount)
                            .IntegerNumber(t => t.FavoriteCount)
                            .DoubleNumber(t => t.Price)
                            .DoubleNumber(t => t.Bathrooms)
                            .DoubleNumber(t => t.SquareFeet)
                            .DoubleNumber(t => t.PricePerSquareFoot)

                            .Boolean(t => t.IsPublished)
                            .Boolean(t => t.IsFeatured)

                            .Date(t => t.CreatedAt)
                            .Date(t => t.UpdatedAt)

                            .Keyword(t => t.AmenityTags)

                            .Nested(t => t.Images)
                    )
                )
                .Settings(s => s
                    .NumberOfShards(3)
                    .NumberOfReplicas(1)
                    .Analysis(a => a
                        .Analyzers(an => an
                            .Custom("autocomplete", ca => ca
                                .Tokenizer("standard")
                            )
                            .Custom("autocomplete_search", ca => ca
                                .Tokenizer("standard")
                            )
                        )
                        .TokenFilters(tf => tf
                            .EdgeNGram("autocomplete_filter", ng => ng
                                .MinGram(2)
                                .MaxGram(20)
                            )
                        )
                    )
                    .RefreshInterval(new Duration("1s"))

                )
            );

            return response.IsValidResponse;
        }
        public async Task<bool> IndexPropertyAsync(Property property)
        {
            try
            {
                var document = MapToDocument(property);
                var response = await _client.IndexAsync(document, IndexName);

                if (!response.IsValidResponse)
                {
                    _logger.LogError("Failed to index property {PropertyId}: {Error}",
                        property.Id, response.DebugInformation);
                }

                return response.IsValidResponse;
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error indexing property {PropertyId}", property.Id);
                return false;
            }
        }

        public async Task<PropertySearchResponse> SearchPropertiesAsync(PropertySearchRequest request)
        {
            try
            {
                var searchResponse = await _client.SearchAsync<PropertyDTO>(s => s
                    .Indices(IndexName)
                    .From((request.PageNumber - 1) * request.PageSize)
                    .Size(request.PageSize)
                    .Query(q => BuildSearchQuery(q, request))
                    .Sort(BuildSortOptions(request))
                );

                if (!searchResponse.IsValidResponse)
                {
                    _logger.LogError("Search failed: {Error}", searchResponse.DebugInformation);
                    return new PropertySearchResponse();
                }

                var totalPages = (int)Math.Ceiling(searchResponse.Total / (double)request.PageSize);

                return new PropertySearchResponse
                {
                    Properties = searchResponse.Documents.ToList(),
                    TotalCount = searchResponse.Total,
                    PageNumber = request.PageNumber,
                    PageSize = request.PageSize,
                    TotalPages = totalPages
                };
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error during search");
                return new PropertySearchResponse();
            }
        }

        public async Task<bool> DeleteIndexAsync()
        {
            try
            {
                var response = await _client.Indices.DeleteAsync(IndexName);
                return response.IsValidResponse;
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error deleting index");
                return false;
            }
        }

        public async Task<bool> UpdatePropertyAsync(Guid propertyId, Property property)
        {
            try
            {
                var document = MapToDocument(property);

                var response = await _client.UpdateAsync<PropertyDTO, PropertyDTO>(
                    IndexName,
                    propertyId.ToString(),
                    u => u.Doc(document)
                );

                return response.IsValidResponse;
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error updating property {PropertyId}", propertyId);
                return false;
            }
        }

        public async Task<bool> DeletePropertyAsync(Guid propertyId)
        {
            try
            {
                var response = await _client.DeleteAsync(IndexName, propertyId.ToString());
                return response.IsValidResponse;
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error deleting property {PropertyId}", propertyId);
                return false;
            }
        }

        public async Task<bool> UpdatePropertyStatusAsync(Guid propertyId, PropertyStatus status)
        {
            try
            {
                var response = await _client.UpdateAsync<PropertyDTO, object>(
                    IndexName,
                    propertyId.ToString(),
                    u => u.Doc(new { Status = status.ToString() })
                );

                return response.IsValidResponse;
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error updating property status");
                return false;
            }
        }

        public async Task<bool> IncrementViewCountAsync(Guid propertyId)
        {
            try
            {
                var response = await _client.UpdateAsync<PropertyDTO, object>(
                    IndexName,
                    propertyId.ToString(),
                    u => u.Script(s => s
                        .Source("ctx._source.viewCount++")
                    )
                );

                return response.IsValidResponse;
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error incrementing view count");
                return false;
            }
        }

        public async Task<bool> UpdateFavoriteCountAsync(Guid propertyId, int count)
        {
            try
            {
                var response = await _client.UpdateAsync<PropertyDTO, object>(
                    IndexName,
                    propertyId.ToString(),
                    u => u.Doc(new { FavoriteCount = count })
                );

                if (!response.IsValidResponse)
                {
                    _logger.LogError("Failed to update favorite count for {PropertyId}: {Error}",
                        propertyId, response.DebugInformation);
                }

                return response.IsValidResponse;
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error updating favorite count for {PropertyId}", propertyId);
                return false;
            }
        }

        public async Task<bool> BulkIndexPropertiesAsync(IEnumerable<Property> properties)
        {
            try
            {
                var documents = properties.Select(MapToDocument).ToList();

                var response = await _client.BulkAsync(b => b
                    .Index(IndexName)
                    .IndexMany(documents)
                );

                if (response.Errors)
                {
                    foreach (var item in response.ItemsWithErrors)
                    {
                        _logger.LogError("Bulk index error for {Id}: {Error}",
                            item.Id, item.Error?.Reason);
                    }
                }

                return response.IsValidResponse;
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error during bulk indexing");
                return false;
            }
        }

        public async Task<bool> ReindexAllPropertiesAsync()
        {
            try
            {
                _logger.LogInformation("Starting full reindex...");

                if (await IndexExistsAsync())
                {
                    await DeleteIndexAsync();
                }

                await CreateIndexAsync();

                var properties = await _unitOfWork.Properties.GetAllPublishedAsync();

                var success = await BulkIndexPropertiesAsync(properties);

                _logger.LogInformation("Reindex completed. Success: {Success}", success);
                return success;
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error during reindex");
                return false;
            }
        }

        public async Task<List<string>> AutocompleteAsync(string query, int limit = 10)
        {
            var searchResponse = await _client.SearchAsync<PropertyDTO>(s => s
                .Indices(IndexName)
                .Size(limit)
                .Query(q => q
                    .Bool(b => b
                        .Must(
                            q.Match(m => m
                                .Field("title.autocomplete")
                                .Query(query)
                            )
                        )
                        .Filter(q.Term(t => t.Field(f => f.IsPublished).Value(true)))
                    )
                )
                .Source(false)
                .Fields(f => f.Field(p => p.Title))
            );

            return searchResponse.Documents
                .Select(d => d.Title)
                .Distinct()
                .ToList();
        }

        public async Task<List<PropertyDTO>> SearchByRadiusAsync(
     double latitude,
     double longitude,
     double radiusKm,
     int limit = 50)
        {
            try
            {
                var searchResponse = await _client.SearchAsync<PropertyDTO>(s => s
                    .Indices(IndexName)
                    .Size(limit)
                    .Query(q => q
                        .Bool(b => b
                            .Must(
                                q.Term(t => t.Field(f => f.IsPublished).Value(true)),
                                q.GeoDistance(gd => gd
                                    .Field(f => f.Location)
                                    .Location(new LatLonGeoLocation
                                    {
                                        Lat = latitude,
                                        Lon = longitude
                                    })
                                    .Distance($"{radiusKm}km")
                                )
                            )
                        )
                    )
                    .Sort(s => s
                        .GeoDistance(new GeoDistanceSort
                        {
                            Field = "location",
                            Location = new[] { ElasticGeoLocation.LatitudeLongitude(
                        new LatLonGeoLocation { Lat = latitude, Lon = longitude }
                    ) },
                            Order = SortOrder.Asc
                        })
                    )
                );

                if (!searchResponse.IsValidResponse)
                {
                    _logger.LogError("Radius search failed: {Error}", searchResponse.DebugInformation);
                    return new List<PropertyDTO>();
                }

                return searchResponse.Documents.ToList();
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error during radius search");
                return new List<PropertyDTO>();
            }
        }

        public async Task<List<PropertyDTO>> SearchByBoundingBoxAsync(
            double topLeftLat,
            double topLeftLon,
            double bottomRightLat,
            double bottomRightLon,
            int limit = 100)
        {
            try
            {
                var searchResponse = await _client.SearchAsync<PropertyDTO>(s => s
                    .Indices(IndexName)
                    .Size(limit)
                    .Query(q => q
                        .Bool(b => b
                            .Must(
                                q.Term(t => t.Field(f => f.IsPublished).Value(true)),
                                q.GeoBoundingBox(gbb => gbb
                                    .Field(f => f.Location)
                                    .BoundingBox(new ElasticBoundingBox
                                    {
                                        TopLeft = ElasticGeoLocation.LatitudeLongitude(
                                            new LatLonGeoLocation { Lat = topLeftLat, Lon = topLeftLon }
                                        ),
                                        BottomRight = ElasticGeoLocation.LatitudeLongitude(
                                            new LatLonGeoLocation { Lat = bottomRightLat, Lon = bottomRightLon }
                                        )

                                    })
                                )
                            )
                        )
                    )
                );

                if (!searchResponse.IsValidResponse)
                {
                    _logger.LogError("Bounding box search failed: {Error}", searchResponse.DebugInformation);
                    return new List<PropertyDTO>();
                }

                return searchResponse.Documents.ToList();
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error during bounding box search");
                return new List<PropertyDTO>();
            }
        }

        public async Task<List<PropertyDTO>> GetSimilarPropertiesAsync(Guid propertyId, int limit = 10)
        {
            // First, get the source property
            var getResponse = await _client.GetAsync<PropertyDTO>(propertyId.ToString(), g => g.Index(IndexName));

            if (!getResponse.IsValidResponse || getResponse.Source == null)
            {
                return new List<PropertyDTO>();
            }

            var sourceProperty = getResponse.Source;

            // Search for similar properties using More Like This
            var searchResponse = await _client.SearchAsync<PropertyDTO>(s => s
                .Indices(IndexName)
                .Size(limit)
                .Query(q => q
                    .Bool(b => b
                        .Must(
                            q.Term(t => t.Field(f => f.IsPublished).Value(true)),
                            q.MoreLikeThis(mlt => mlt
                                .Fields(new[] { "title", "description", "city" })
// Replace this line:
//.Like(new Like[] { new LikeDocument<PropertyDTO>(propertyId.ToString()) })

// With this:
.Like(new Like[] { new LikeDocument { Id = propertyId.ToString(), Index = IndexName } })
                                .MinTermFreq(1)
                                .MinDocFreq(1)
                            )
                        )
                        .Filter(
                            // Same property type
                            q.Term(t => t.Field(f => f.PropertyType).Value(sourceProperty.PropertyType.ToString())),
                            // Similar price range (+/- 30%)
                            q.Range(r => r.Number(nr => nr
                                .Field(f => f.Price)
                                .Gte((double)(sourceProperty.Price * 0.7m))
                                .Lte((double)(sourceProperty.Price * 1.3m))
                            ))
                        )
                        .MustNot(
                            // Exclude the source property itself
                            q.Ids(new IdsQuery { Values = new[] { propertyId.ToString() } })
                        )
                    )
                )
            );

            return searchResponse.Documents.ToList();
        }

        public async Task<List<PropertyDTO>> GetRecommendedPropertiesAsync(string userId, int limit = 10)
        {
            // This is a simplified recommendation system
            // In production, you'd use user browsing history, favorites, etc.

            var searchResponse = await _client.SearchAsync<PropertyDTO>(s => s
                .Indices(IndexName)
                .Size(limit)
                .Query(q => q
                    .Bool(b => b
                        .Must(q.Term(t => t.Field(f => f.IsPublished).Value(true)))
                        .Should(
                            q.Term(t => t.Field(f => f.IsFeatured).Value(true).Boost(2)),
                            q.Range(r => r.Date(dr => dr
                                .Field(f => f.CreatedAt)
                                .Gte(DateMath.Now.Subtract("7d"))
                                .Boost(1)
                            ))
                        )
                    )
                )
                .Sort(s => s.Score(new ScoreSort { Order = SortOrder.Desc }))
            );

            return searchResponse.Documents.ToList();
        }


        public async Task<List<string>> GetLocationSuggestionsAsync(string query, int limit = 10)
        {
            var searchResponse = await _client.SearchAsync<PropertyDTO>(s => s
                .Indices(IndexName)
                .Size(0) // We only want aggregations
                .Query(q => q
                    .Bool(b => b
                        .Must(
                            q.Term(t => t.Field(f => f.IsPublished).Value(true)),
                            q.MultiMatch(mm => mm
                                .Query(query)
                                .Fields(new[] { "city", "state", "fullAddress" })
                                .Type(TextQueryType.BoolPrefix)
                            )
                        )
                    )
                )
                .Aggregations(a => a
                    .Add("cities", new TermsAggregation
                    {
                    
                        Field = "city",
                        Size = limit
                    })
                )
            );

            if (searchResponse.Aggregations?.TryGetValue("cities", out var citiesAgg) == true &&
                citiesAgg is StringTermsAggregate cities)
            {
                return cities.Buckets.Select(b => b.Key.ToString()).ToList();
            }

            return new List<string>();
        }


        private PropertyDTO MapToDocument(Property property)
        {
            var primaryImage = property.Images?.FirstOrDefault(i => i.IsPrimary);

            var amenityTags = new List<string>();
            if (property.Amenities != null)
            {
                foreach (var amenity in property.Amenities)
                {
                    foreach (var kvp in amenity.Amenities)
                    {
                        if (kvp.Value is bool boolValue && boolValue)
                        {
                            amenityTags.Add(kvp.Key.ToLower());
                        }
                    }
                }
            }

            var fullAddress = string.Join(", ", new[]
            {
                property.Street,
                property.City,
                property.State,
                property.PostalCode,
                property.Country
            }.Where(s => !string.IsNullOrWhiteSpace(s)));

            var pricePerSqFt = property.SquareFeet > 0
                ? (double)(property.Price / property.SquareFeet)
                : 0;

            var daysOnMarket = property.PublishedAt.HasValue
                ? (DateTime.UtcNow - property.PublishedAt.Value).Days
                : 0;

            return new PropertyDTO
            {
                Id = property.Id,
                AgentId = property.AgentId,
                Title = property.Title,
                Description = property.Description,
                
                PropertyType = property.PropertyType,
                ListingType = property.ListingType,

                Street = property.Street,
                City = property.City,
                State = property.State,
                Country = property.Country,
                PostalCode = property.PostalCode,
                FullAddress = fullAddress,

                Location = ElasticGeoLocation.LatitudeLongitude(
                    new LatLonGeoLocation
                    {
                        Lat = (double)property.Latitude,
                        Lon = (double)property.Longitude
                    }
                ),

                Bedrooms = property.Bedrooms,
                Bathrooms = property.Bathrooms,
                SquareFeet = property.SquareFeet,
                LotSize = property.LotSize,
                YearBuilt = property.YearBuilt,

                Price = property.Price,
                Currency = property.Currency,

                Status = property.Status.ToString(),
                IsPublished = property.IsPublished,
                IsFeatured = property.IsFeatured,

                ViewCount = property.ViewCount,
                FavoriteCount = property.FavoriteCount,

                Images = property.Images?.Select(i => new PropertyImageDTO
                {
                    Id = i.Id,
                    ImageUrl = i.ImageUrl,
                    ThumbnailUrl = i.ThumbnailUrl,
                    DisplayOrder = i.DisplayOrder,
                    IsPrimary = i.IsPrimary
                }).ToList() ?? new List<PropertyImageDTO>(),

                PrimaryImageUrl = primaryImage?.ImageUrl,

                AmenityTags = amenityTags,
                AmenitiesData = property.Amenities?.ToDictionary(
                    a => a.Category.ToString(),
                    a => (object)a.Amenities
                ) ?? new Dictionary<string, object>(),

                CreatedAt = property.CreatedAt,
                UpdatedAt = property.UpdatedAt,
                PublishedAt = property.PublishedAt,

                PricePerSquareFoot = pricePerSqFt,
                DaysOnMarket = daysOnMarket
            };
        }

        private Query BuildSearchQuery(QueryDescriptor<PropertyDTO> q, PropertySearchRequest request)
        {
            var mustQueries = new List<Query>();
            var filterQueries = new List<Query>();

            filterQueries.Add(q.Term(t => t.Field(f => f.IsPublished).Value(true)));

            if (!string.IsNullOrWhiteSpace(request.Query))
            {
                mustQueries.Add(q.MultiMatch(m => m
                    .Query(request.Query)
                    .Fields(new[] { "title^3", "description^2", "fullAddress", "city^2" })
                    .Type(TextQueryType.BestFields)
                    .Fuzziness(new Fuzziness("AUTO"))
                ));
            }

            if (!string.IsNullOrWhiteSpace(request.City))
            {
                filterQueries.Add(q.Term(t => t.Field(f => f.City).Value(request.City)));
            }

            if (!string.IsNullOrWhiteSpace(request.State))
            {
                filterQueries.Add(q.Term(t => t.Field(f => f.State).Value(request.State)));
            }

            if (request.MinPrice.HasValue || request.MaxPrice.HasValue)
            {
                filterQueries.Add(q.Range(r => r.Number(nr => nr
                    .Field(f => f.Price)
                    .Gte(request.MinPrice.HasValue ? (double)request.MinPrice.Value : null)
                    .Lte(request.MaxPrice.HasValue ? (double)request.MaxPrice.Value : null)
                )));
            }

            if (request.MinBedrooms.HasValue)
            {
                filterQueries.Add(q.Range(r => r.Number(nr => nr
                    .Field(f => f.Bedrooms)
                    .Gte(request.MinBedrooms.Value)
                )));
            }

            if (request.PropertyTypes?.Any() == true)
            {
                filterQueries.Add(q.Terms(t => t
                    .Field(f => f.PropertyType)
                    .Terms(new TermsQueryField(request.PropertyTypes.Select(pt => FieldValue.String(pt)).ToArray()))
                ));
            }

            if (request.Amenities?.Any() == true)
            {
                filterQueries.Add(q.Terms(t => t
                    .Field(f => f.AmenityTags)
                    .Terms(new TermsQueryField(request.Amenities.Select(a => FieldValue.String(a.ToLower())).ToArray()))
                ));
            }

            var boolQuery = new BoolQuery();

            if (mustQueries.Any())
            {
                boolQuery.Must = mustQueries;
            }

            if (filterQueries.Any())
            {
                boolQuery.Filter = filterQueries;
            }

            return q.Bool(boolQuery);

        }

        private SortOptionsDescriptor<PropertyDTO> BuildSortOptions(PropertySearchRequest request)
        {
            var sortDescriptor = new SortOptionsDescriptor<PropertyDTO>();

            switch (request.SortBy?.ToLower())
            {
                case "price":
                    sortDescriptor.Field(f => f
                        .Field(p => p.Price)
                        .Order(request.SortOrder?.ToLower() == "desc" ? SortOrder.Desc : SortOrder.Asc)
                    );
                    break;

                case "newest":
                    sortDescriptor.Field(f => f
                        .Field(p => p.CreatedAt)
                        .Order(SortOrder.Desc)
                    );
                    break;

                case "popular":
                    sortDescriptor.Field(f => f
                        .Field(p => p.ViewCount)
                        .Order(SortOrder.Desc)
                    );
                    break;

                default: // relevance
                    sortDescriptor.Score(s => s.Order(SortOrder.Desc));
                    break;
            }

            return sortDescriptor;
        }

    }
}
