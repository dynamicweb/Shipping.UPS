using Dynamicweb.Core;
using Dynamicweb.Ecommerce.Orders;
using Dynamicweb.Environment;
using System;
using System.Collections.Generic;
using System.Linq;

namespace Dynamicweb.Ecommerce.ShippingProviders.UPS
{
    /// <summary>
    /// Provides methods for caching request data
    /// </summary>
    internal static class ShippingProviderHelper
    {
        #region Nested types

        /// <summary>
        /// Structure that is used while calculating shipping fee for the specified order in FedEx shipping provider
        /// </summary>
        internal struct RateRequest
        {
            public string Request;
            public double Rate;
            public string Currency;
            public List<string> Errors;
            public List<string> Warning;
        }

        #endregion

        #region Cache rate request

        private static string ShippingCacheKey(string shippingID) => string.Format("ShippingServiceRateRequest_{0}", shippingID);

        /// <summary>
        /// Checks if rate request is cached
        /// </summary>
        /// <param name="shippingID">identifier of shipping</param>
        /// <param name="request">request</param>
        /// <returns>Rate request instance</returns>
        public static RateRequest CheckIsRateRequestCached(string shippingID, string request, IContext context)
        {
            var rateRequest = default(RateRequest);
            var session = context?.Session;

            if (session is not null && session[ShippingCacheKey(shippingID)] is RateRequest cachedRequest)
            {
                if (request == cachedRequest.Request)
                {
                    rateRequest = cachedRequest;
                }
            }

            return rateRequest;
        }

        /// <summary>
        /// Adds rate request to cache
        /// </summary>
        /// <param name="shippingID">identifier of shipping</param>
        /// <param name="request">Request</param>
        /// <param name="rate">Rate</param>
        /// <param name="currency">Currency</param>
        /// <param name="warning">list of warnings</param>
        /// <param name="errors">list of errors</param>
        public static void CacheRateRequest(string shippingID, string request, double rate, string currency, List<string> warning, List<string> errors)
        {
            var context = Context.Current;
            var session = context?.Session;
            if (session is null)
            {
                return;
            }

            session[ShippingCacheKey(shippingID)] = new RateRequest
            {
                Request = request,
                Rate = rate,
                Currency = currency,
                Warning = new List<string>(warning),
                Errors = new List<string>(errors)
            };
        }

        /// <summary>
        /// Gets information about shipping request processing status
        /// </summary>
        /// <param name="shippingID">identifier of shipping</param>
        /// <returns>true if shipping was processed</returns>
        public static bool IsThisShippingRequestWasProcessed(string shippingID, IContext context)
        {
            return context.Items.Contains(ShippingCacheKey(shippingID));
        }

        /// <summary>
        /// Marks shipping request status as "in progress"
        /// </summary>
        /// <param name="shippingID"></param>
        public static void SetShippingRequestIsProcessed(string shippingID, IContext context)
        {
            if (!IsThisShippingRequestWasProcessed(shippingID, context))
            {
                context.Items.Add(ShippingCacheKey(shippingID), true);
            }
        }

        #endregion

        /// <summary>
        /// Split products by packages
        /// </summary>
		/// <param name="order">The order</param>
        /// <param name="groupByManufacturer">Group by manufacturer or not</param>
        /// <param name="numberOfProductsPerPackage">Number of products per package</param>
        /// <returns>List of packages weights</returns>
        public static List<double> SplitProductsByPackages(Order order, bool groupByManufacturer, int numberOfProductsPerPackage)
        {
            if (groupByManufacturer || numberOfProductsPerPackage <= 0)
            {
                numberOfProductsPerPackage = int.MaxValue;
            }

            var packagesWeight = new List<double>();
            Func<OrderLine, string> groupFunc;

            if (groupByManufacturer)
            {
                groupFunc = orderLine => Converter.ToString(orderLine.Product.ManufacturerId);
            }
            else
            {
                groupFunc = orderLine => string.Empty;
            }

            var groups = order.OrderLines.Where(orderLine => orderLine.Product != null).GroupBy(groupFunc);

            foreach (var group in groups)
            {
                double currentPackageWeight = 0;
                double currentPackageQuatity = 0;

                foreach (var orderLine in group)
                {
                    if (orderLine.HasType(OrderLineType.Product) ||
                        orderLine.HasType(OrderLineType.PointProduct) ||
                        orderLine.HasType(OrderLineType.Fixed))
                    {
                        double overageQuatity = orderLine.Quantity + currentPackageQuatity - numberOfProductsPerPackage;
                        if (overageQuatity < 0)
                        {
                            overageQuatity = 0;
                        }
                        double lineQuantity = orderLine.Quantity - overageQuatity;

                        currentPackageQuatity += lineQuantity;
                        currentPackageWeight += orderLine.Product.Weight * lineQuantity;
                        if (currentPackageQuatity == numberOfProductsPerPackage)
                        {
                            packagesWeight.Add(currentPackageWeight);

                            while (overageQuatity >= numberOfProductsPerPackage)
                            {
                                overageQuatity -= numberOfProductsPerPackage;
                                packagesWeight.Add(orderLine.Product.Weight * numberOfProductsPerPackage);
                            }

                            if (overageQuatity > 0)
                            {
                                currentPackageQuatity = overageQuatity;
                                currentPackageWeight = orderLine.Product.Weight * lineQuantity;
                            }
                            else
                            {
                                currentPackageQuatity = 0;
                                currentPackageWeight = 0;
                            }
                        }
                    }
                }

                if (currentPackageWeight > 0)
                {
                    packagesWeight.Add(currentPackageWeight);
                }
            }

            return packagesWeight;
        }
    }
}
