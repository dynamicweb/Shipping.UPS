using Dynamicweb.Core;
using Dynamicweb.Ecommerce.Cart;
using Dynamicweb.Ecommerce.Orders;
using Dynamicweb.Ecommerce.Prices;
using Dynamicweb.Environment;
using Dynamicweb.Extensibility.AddIns;
using Dynamicweb.Extensibility.Editors;
using System;
using System.Collections.Generic;
using System.Net.Http;
using System.Net.Http.Headers;
using System.Threading.Tasks;
using System.Xml;

namespace Dynamicweb.Ecommerce.ShippingProviders.UPS
{
    /// <summary>
    /// UPS Shipping Service
    /// </summary>
    [AddInName("UPS"), AddInDescription("UPS Delivery provider")]
    public class UPS : ShippingProvider, IParameterOptions
    {
        #region Parameters

        [AddInParameter("User ID"), AddInParameterEditor(typeof(TextParameterEditor), "size=80")]
        public string UserID { get; set; } = "";

        [AddInParameter("Password"), AddInParameterEditor(typeof(TextParameterEditor), "size=80")]
        public string Password { get; set; } = "";

        [AddInParameter("Access Key"), AddInParameterEditor(typeof(TextParameterEditor), "size=80")]
        public string AccessKey { get; set; } = "";

        [AddInParameter("Rate service URL"), AddInParameterEditor(typeof(TextParameterEditor), "size=80")]
        public string ServiceURL { get; set; }

        [AddInParameter("Delivery Service"), AddInParameterEditor(typeof(DropDownParameterEditor), "SortBy=Value")]
        public string DeliveryService { get; set; } = "";

        [AddInParameter("Pickup Type"), AddInParameterEditor(typeof(DropDownParameterEditor), "SortBy=Value")]
        public string PickupType { get; set; } = "";

        [AddInParameter("Container Type"), AddInParameterEditor(typeof(DropDownParameterEditor), "SortBy=Value")]
        public string ContainerType { get; set; } = "";

        [AddInParameter("Customer Classification"), AddInParameterEditor(typeof(DropDownParameterEditor), "SortBy=Value")]
        public string CustomerClassification { get; set; } = "";

        [AddInParameter("UPS Account Number"), AddInParameterEditor(typeof(TextParameterEditor), "")]
        public string ShipperNumber { get; set; } = "";

        [AddInParameter("Group by manufacturer"), AddInParameterEditor(typeof(YesNoParameterEditor), "")]
        public bool GroupByManufacturer { get; set; }

        [AddInParameter("Number Of Products Per Package"), AddInParameterEditor(typeof(TextParameterEditor), "")]
        public string NumberOfProductsPerPackage { get; set; } = "";

        [AddInParameter("Dimensions Unit"), AddInParameterEditor(typeof(DropDownParameterEditor), "")]
        public string DimensionsUnitOfMeasurement { get; set; } = "";

        [AddInParameter("Default Length"), AddInParameterEditor(typeof(TextParameterEditor), "")]
        public string Length { get; set; }

        [AddInParameter("Default Width"), AddInParameterEditor(typeof(TextParameterEditor), "")]
        public string Width { get; set; }

        [AddInParameter("Default Height"), AddInParameterEditor(typeof(TextParameterEditor), "")]
        public string Height { get; set; }

        [AddInParameter("Weight Unit"), AddInParameterEditor(typeof(DropDownParameterEditor), "")]
        public string WeightUnitOfMeasurement { get; set; } = "";

        [AddInParameter("Company Name"), AddInParameterEditor(typeof(TextParameterEditor), "")]
        public string CompanyName { get; set; } = "";

        [AddInParameter("Attention Name"), AddInParameterEditor(typeof(TextParameterEditor), "")]
        public string AttentionName { get; set; } = "";

        [AddInParameter("Phone Number"), AddInParameterEditor(typeof(TextParameterEditor), "")]
        public string PhoneNumber { get; set; } = "";

        [AddInParameter("Fax Number"), AddInParameterEditor(typeof(TextParameterEditor), "")]
        public string FaxNumber { get; set; } = "";

        [AddInParameter("Origination Street Address"), AddInParameterEditor(typeof(TextParameterEditor), "")]
        public string StreetAddress { get; set; } = "";

        [AddInParameter("Origination Street Address 2"), AddInParameterEditor(typeof(TextParameterEditor), "")]
        public string StreetAddress2 { get; set; } = "";

        [AddInParameter("Origination City"), AddInParameterEditor(typeof(TextParameterEditor), "")]
        public string City { get; set; } = "";

        [AddInParameter("Origination State"), AddInParameterEditor(typeof(DropDownParameterEditor), "SortBy=Value")]
        public string StateProvinceCode { get; set; } = "";

        [AddInParameter("Origination Zip Code"), AddInParameterEditor(typeof(TextParameterEditor), "")]
        public string PostalCode { get; set; } = "";

        [AddInParameter("XML Log"), AddInParameterEditor(typeof(YesNoParameterEditor), ""), AddInDescription("Create a log of the request and response from UPS")]
        public bool XMLLog { get; set; }

        #endregion

        /// <summary>
        /// Default constructor
        /// </summary>
        public UPS()
        {
            Length = "10";
            Width = "10";
            Height = "10";
            ServiceURL = "https://wwwcie.ups.com/ups.app/xml/Rate";
        }

        #region CalculateShippingFee

        /// <summary>
        /// Calculate shipping fee for the specified order
        /// </summary>
        /// <param name="Order">The order.</param>
        /// <returns>Returns shipping fee for the specified order</returns>
        public override PriceRaw? CalculateShippingFee(Order order)
        {
            var shippingXml = string.Empty;
            PriceRaw? rate = null;
            order.ShippingProviderErrors.Clear();
            order.ShippingProviderWarnings.Clear();
            var context = Context.Current;
            if (context is null)
                throw new ContextUnavailableException();

            try
            {
                if (IsRequestParametersCorrect(order))
                {
                    var sequrityXmlString = string.Empty;
                    shippingXml = CreateRequest(order, ref sequrityXmlString);

                    var rateRequest = ShippingProviderHelper.CheckIsRateRequestCached(ShippingID, shippingXml, context);
                    if (rateRequest.Rate > 0 || ShippingProviderHelper.IsThisShippingRequestWasProcessed(ShippingID, context))
                    {
                        rate = new PriceRaw(rateRequest.Rate, Services.Currencies.GetCurrency(rateRequest.Currency));
                        if (rateRequest.Warning != null)
                        {
                            order.ShippingProviderWarnings.AddRange(rateRequest.Warning);
                        }
                        if (rateRequest.Errors != null)
                        {
                            order.ShippingProviderErrors.AddRange(rateRequest.Errors);
                        }
                    }
                    else
                    {
                        if (XMLLog) SaveLog(shippingXml, true);

                        var xmlDocument = new XmlDocument();
                        
                        using (var webResponse = Task.Run(() => ProcessRequest(sequrityXmlString, shippingXml)).Result)
                        {
                            xmlDocument.Load(Task.Run(() => webResponse.Content.ReadAsStringAsync()).Result);
                        }
                        if (XMLLog) SaveXmlLog(xmlDocument, false);
                        rate = ProcessResponse(xmlDocument, order);
                    }
                }
            }
            catch (Exception err)
            {
                if (XMLLog) SaveLog(err.Message, true);
                order.ShippingProviderErrors.Add(err.Message);
            }

            ShippingProviderHelper.CacheRateRequest(ShippingID, shippingXml, rate == null ? 0 : rate.Price, rate == null ? order.CurrencyCode : rate.Currency.Code, order.ShippingProviderWarnings, order.ShippingProviderErrors);
            ShippingProviderHelper.SetShippingRequestIsProcessed(ShippingID, context);

            return rate;
        }

        private bool IsRequestParametersCorrect(Dynamicweb.Ecommerce.Orders.Order order)
        {
            if (string.IsNullOrEmpty(order.DeliveryZip) && string.IsNullOrEmpty(order.CustomerZip))
            {
                order.ShippingProviderErrors.Add("ZipCode field is empty.");
            }

            string country = string.IsNullOrEmpty(order.DeliveryCountryCode) ? order.CustomerCountryCode : order.DeliveryCountryCode;
            if (country != "US")
            {
                order.ShippingProviderErrors.Add("Only USA country is allowed for delivering.");
            }

            return order.ShippingProviderErrors.Count == 0;
        }

        private string CreateRequest(Order order, ref string sequrityRequest)
        {
            XmlDocument sequrityXmlDocument = new XmlDocument();
            XmlElement rootSecurityNode = sequrityXmlDocument.CreateElement("AccessRequest");

            XmlElement userIdNode = sequrityXmlDocument.CreateElement("UserId");
            XmlElement passwordNode = sequrityXmlDocument.CreateElement("Password");
            XmlElement accessLicenseNode = sequrityXmlDocument.CreateElement("AccessLicenseNumber");

            userIdNode.InnerText = UserID;
            passwordNode.InnerText = Password;
            accessLicenseNode.InnerText = AccessKey;

            rootSecurityNode.AppendChild(userIdNode);
            rootSecurityNode.AppendChild(passwordNode);
            rootSecurityNode.AppendChild(accessLicenseNode);

            sequrityXmlDocument.AppendChild(rootSecurityNode);

            #region Rating service selection body

            XmlDocument xmlDocument = new XmlDocument();
            XmlElement rootNode = xmlDocument.CreateElement("RatingServiceSelectionRequest");

            XmlElement requestNode = xmlDocument.CreateElement("Request");
            requestNode.InnerXml =
                @"<TransactionReference>
                    <CustomerContext>Rating and Service</CustomerContext>
                    <XpciVersion>1.0</XpciVersion>
                </TransactionReference>
                <RequestAction>Rate</RequestAction>
                <RequestOption>Rate</RequestOption>";
            rootNode.AppendChild(requestNode);

            XmlElement pickupTypeNode = xmlDocument.CreateElement("PickupType");
            {
                XmlElement pickupTypeCodeNode = xmlDocument.CreateElement("Code");
                pickupTypeCodeNode.InnerText = string.IsNullOrEmpty(PickupType) ? "01" : PickupType;
                pickupTypeNode.AppendChild(pickupTypeCodeNode);
            }
            rootNode.AppendChild(pickupTypeNode);

            XmlElement customerClassificationNode = xmlDocument.CreateElement("CustomerClassification");
            {
                XmlElement customerClassificationCodeNode = xmlDocument.CreateElement("Code");
                if (string.IsNullOrEmpty(CustomerClassification))
                {
                    if (string.IsNullOrEmpty(PickupType) || PickupType == "01")
                    {
                        customerClassificationCodeNode.InnerText = "01";
                    }
                    else if (PickupType != "03")
                    {
                        customerClassificationCodeNode.InnerText = "04";
                    }
                }
                else
                {
                    customerClassificationCodeNode.InnerText = CustomerClassification;
                }
                customerClassificationNode.AppendChild(customerClassificationCodeNode);
            }
            rootNode.AppendChild(customerClassificationNode);

            XmlElement shipmentNode = xmlDocument.CreateElement("Shipment");
            {
                XmlElement shipmentDescriptionNode = xmlDocument.CreateElement("Description");
                shipmentDescriptionNode.InnerText = "Rate Shopping - Domestic";
                shipmentNode.AppendChild(shipmentDescriptionNode);

                XmlElement shipperNode = xmlDocument.CreateElement("Shipper");
                {
                    XmlElement shipperNameNode = xmlDocument.CreateElement("Name");
                    shipperNameNode.InnerText = CompanyName;
                    shipperNode.AppendChild(shipperNameNode);

                    XmlElement shipperNumberNode = xmlDocument.CreateElement("ShipperNumber");
                    shipperNumberNode.InnerText = ShipperNumber;
                    shipperNode.AppendChild(shipperNumberNode);

                    XmlElement shipperAddressNode = xmlDocument.CreateElement("Address");
                    {
                        XmlElement shipperAddressLine1Node = xmlDocument.CreateElement("AddressLine1");
                        shipperAddressLine1Node.InnerText = StreetAddress;
                        shipperAddressNode.AppendChild(shipperAddressLine1Node);

                        XmlElement shipperAddressLine2Node = xmlDocument.CreateElement("AddressLine2");
                        shipperAddressLine2Node.InnerText = StreetAddress2;
                        shipperAddressNode.AppendChild(shipperAddressLine2Node);

                        XmlElement shipperCityNode = xmlDocument.CreateElement("City");
                        shipperCityNode.InnerText = City;
                        shipperAddressNode.AppendChild(shipperCityNode);

                        XmlElement shipperStateNode = xmlDocument.CreateElement("StateProvinceCode");
                        shipperStateNode.InnerText = StateProvinceCode;
                        shipperAddressNode.AppendChild(shipperStateNode);

                        XmlElement shipperPostalCodeNode = xmlDocument.CreateElement("PostalCode");
                        shipperPostalCodeNode.InnerText = PostalCode;
                        shipperAddressNode.AppendChild(shipperPostalCodeNode);

                        XmlElement shipperCountryCodeNode = xmlDocument.CreateElement("CountryCode");
                        shipperCountryCodeNode.InnerText = "US";
                        shipperAddressNode.AppendChild(shipperCountryCodeNode);
                    }
                    shipperNode.AppendChild(shipperAddressNode);
                }
                shipmentNode.AppendChild(shipperNode);

                XmlElement shipToNode = xmlDocument.CreateElement("ShipTo");
                {
                    bool isDeliveryFieldsFilled = !string.IsNullOrEmpty(order.DeliveryZip);

                    XmlElement shipToCompanyNameNode = xmlDocument.CreateElement("CompanyName");
                    shipToCompanyNameNode.InnerText = isDeliveryFieldsFilled ? order.DeliveryCompany : order.CustomerCompany;
                    shipToNode.AppendChild(shipToCompanyNameNode);

                    XmlElement shipToAttentionNameNode = xmlDocument.CreateElement("AttentionName");
                    shipToAttentionNameNode.InnerText = isDeliveryFieldsFilled ? order.DeliveryName : order.CustomerName;
                    shipToNode.AppendChild(shipToAttentionNameNode);

                    XmlElement shipToPhoneNumberNode = xmlDocument.CreateElement("PhoneNumber");
                    shipToPhoneNumberNode.InnerText = isDeliveryFieldsFilled ? string.IsNullOrEmpty(order.DeliveryCell) ? order.DeliveryPhone : order.DeliveryCell : string.IsNullOrEmpty(order.CustomerCell) ? order.CustomerPhone : order.CustomerCell;
                    shipToNode.AppendChild(shipToPhoneNumberNode);

                    XmlElement shipToAddressNode = xmlDocument.CreateElement("Address");
                    {
                        XmlElement shipToAddressLine1Node = xmlDocument.CreateElement("AddressLine1");
                        shipToAddressLine1Node.InnerText = isDeliveryFieldsFilled ? order.DeliveryAddress : order.CustomerAddress;
                        shipToAddressNode.AppendChild(shipToAddressLine1Node);

                        XmlElement shipToAddressLine2Node = xmlDocument.CreateElement("AddressLine2");
                        shipToAddressLine2Node.InnerText = isDeliveryFieldsFilled ? order.DeliveryAddress2 : order.CustomerAddress2;
                        shipToAddressNode.AppendChild(shipToAddressLine2Node);

                        XmlElement shipToCityNode = xmlDocument.CreateElement("City");
                        shipToCityNode.InnerText = isDeliveryFieldsFilled ? order.DeliveryCity : order.CustomerCity;
                        shipToAddressNode.AppendChild(shipToCityNode);

                        XmlElement shipToStateNode = xmlDocument.CreateElement("StateProvinceCode");
                        shipToStateNode.InnerText = isDeliveryFieldsFilled ? order.DeliveryRegion : order.CustomerRegion;
                        shipToAddressNode.AppendChild(shipToStateNode);

                        XmlElement shipToPostalCodeNode = xmlDocument.CreateElement("PostalCode");
                        shipToPostalCodeNode.InnerText = isDeliveryFieldsFilled ? order.DeliveryZip : order.CustomerZip;
                        shipToAddressNode.AppendChild(shipToPostalCodeNode);

                        XmlElement shipToCountryCodeNode = xmlDocument.CreateElement("CountryCode");
                        shipToCountryCodeNode.InnerText = "US";
                        shipToAddressNode.AppendChild(shipToCountryCodeNode);
                    }
                    shipToNode.AppendChild(shipToAddressNode);
                }
                shipmentNode.AppendChild(shipToNode);

                XmlElement shipFromNode = xmlDocument.CreateElement("ShipFrom");
                {
                    XmlElement shipFromCompanyNameNode = xmlDocument.CreateElement("CompanyName");
                    shipFromCompanyNameNode.InnerText = CompanyName;
                    shipFromNode.AppendChild(shipFromCompanyNameNode);

                    XmlElement shipFromAttentionNameNode = xmlDocument.CreateElement("AttentionName");
                    shipFromAttentionNameNode.InnerText = AttentionName;
                    shipFromNode.AppendChild(shipFromAttentionNameNode);

                    XmlElement shipFromPhoneNumberNode = xmlDocument.CreateElement("PhoneNumber");
                    shipFromPhoneNumberNode.InnerText = PhoneNumber;
                    shipFromNode.AppendChild(shipFromPhoneNumberNode);

                    XmlElement shipFromFaxNumberNode = xmlDocument.CreateElement("FaxNumber");
                    shipFromFaxNumberNode.InnerText = FaxNumber;
                    shipFromNode.AppendChild(shipFromFaxNumberNode);

                    XmlElement shipFromAddressNode = xmlDocument.CreateElement("Address");
                    {
                        XmlElement shipFromAddressLine1Node = xmlDocument.CreateElement("AddressLine1");
                        shipFromAddressLine1Node.InnerText = StreetAddress;
                        shipFromAddressNode.AppendChild(shipFromAddressLine1Node);

                        XmlElement shipFromAddressLine2Node = xmlDocument.CreateElement("AddressLine2");
                        shipFromAddressLine2Node.InnerText = StreetAddress2;
                        shipFromAddressNode.AppendChild(shipFromAddressLine2Node);

                        XmlElement shipFromCityNode = xmlDocument.CreateElement("City");
                        shipFromCityNode.InnerText = City;
                        shipFromAddressNode.AppendChild(shipFromCityNode);

                        XmlElement shipFromStateNode = xmlDocument.CreateElement("StateProvinceCode");
                        shipFromStateNode.InnerText = StateProvinceCode;
                        shipFromAddressNode.AppendChild(shipFromStateNode);

                        XmlElement shipFromPostalCodeNode = xmlDocument.CreateElement("PostalCode");
                        shipFromPostalCodeNode.InnerText = PostalCode;
                        shipFromAddressNode.AppendChild(shipFromPostalCodeNode);

                        XmlElement shipFromCountryCodeNode = xmlDocument.CreateElement("CountryCode");
                        shipFromCountryCodeNode.InnerText = "US";
                        shipFromAddressNode.AppendChild(shipFromCountryCodeNode);
                    }
                    shipFromNode.AppendChild(shipFromAddressNode);
                }
                shipmentNode.AppendChild(shipFromNode);

                XmlElement shipmentServiceNode = xmlDocument.CreateElement("Service");
                {
                    XmlElement shipmentServiceCodeNode = xmlDocument.CreateElement("Code");
                    shipmentServiceCodeNode.InnerText = DeliveryService;
                    shipmentServiceNode.AppendChild(shipmentServiceCodeNode);
                }
                shipmentNode.AppendChild(shipmentServiceNode);

                List<double> packagesWeight = ShippingProviderHelper.SplitProductsByPackages(order, GroupByManufacturer, Converter.ToInt32(NumberOfProductsPerPackage));
                foreach (double pakageWeight in packagesWeight)
                {
                    XmlElement shipmentPackageNode = xmlDocument.CreateElement("Package");
                    {
                        XmlElement shipmentPackagingTypeNode = xmlDocument.CreateElement("PackagingType");
                        {
                            XmlElement shipmentPackagingTypeCodeNode = xmlDocument.CreateElement("Code");
                            shipmentPackagingTypeCodeNode.InnerText = ContainerType;
                            shipmentPackagingTypeNode.AppendChild(shipmentPackagingTypeCodeNode);
                        }
                        shipmentPackageNode.AppendChild(shipmentPackagingTypeNode);

                        XmlElement shipmentPackageDescriptionNode = xmlDocument.CreateElement("Description");
                        shipmentPackageDescriptionNode.InnerText = "Rate";
                        shipmentPackageNode.AppendChild(shipmentPackageDescriptionNode);

                        XmlElement shipmentPackageDimensionsNode = xmlDocument.CreateElement("Dimensions");
                        {
                            XmlElement shipmentPackageUnitOfMeasurementNode = xmlDocument.CreateElement("UnitOfMeasurement");
                            {
                                XmlElement shipmentPackageUnitOfMeasurementCodeNode = xmlDocument.CreateElement("Code");
                                shipmentPackageUnitOfMeasurementCodeNode.InnerText = DimensionsUnitOfMeasurement;
                                shipmentPackageUnitOfMeasurementNode.AppendChild(shipmentPackageUnitOfMeasurementCodeNode);
                            }
                            shipmentPackageDimensionsNode.AppendChild(shipmentPackageUnitOfMeasurementNode);

                            XmlElement shipmentPackageDimensionsLengthNode = xmlDocument.CreateElement("Length");
                            shipmentPackageDimensionsLengthNode.InnerText = Length;
                            shipmentPackageDimensionsNode.AppendChild(shipmentPackageDimensionsLengthNode);

                            XmlElement shipmentPackageDimensionsWidthNode = xmlDocument.CreateElement("Width");
                            shipmentPackageDimensionsWidthNode.InnerText = Width;
                            shipmentPackageDimensionsNode.AppendChild(shipmentPackageDimensionsWidthNode);

                            XmlElement shipmentPackageDimensionsHeightNode = xmlDocument.CreateElement("Height");
                            shipmentPackageDimensionsHeightNode.InnerText = Height;
                            shipmentPackageDimensionsNode.AppendChild(shipmentPackageDimensionsHeightNode);
                        }
                        shipmentPackageNode.AppendChild(shipmentPackageDimensionsNode);

                        XmlElement shipmentPackageWeightNode = xmlDocument.CreateElement("PackageWeight");
                        {
                            XmlElement shipmentWeightUnitOfMeasurementNode = xmlDocument.CreateElement("UnitOfMeasurement");
                            {
                                XmlElement shipmentWeightUnitOfMeasurementCodeNode = xmlDocument.CreateElement("Code");
                                shipmentWeightUnitOfMeasurementCodeNode.InnerText = WeightUnitOfMeasurement;
                                shipmentWeightUnitOfMeasurementNode.AppendChild(shipmentWeightUnitOfMeasurementCodeNode);
                            }
                            shipmentPackageWeightNode.AppendChild(shipmentWeightUnitOfMeasurementNode);

                            XmlElement shipmentPackageWeightValueNode = xmlDocument.CreateElement("Weight");
                            shipmentPackageWeightValueNode.InnerText = pakageWeight.ToString();
                            shipmentPackageWeightNode.AppendChild(shipmentPackageWeightValueNode);
                        }
                        shipmentPackageNode.AppendChild(shipmentPackageWeightNode);
                    }
                    shipmentNode.AppendChild(shipmentPackageNode);
                }
            }

            rootNode.AppendChild(shipmentNode);
            xmlDocument.AppendChild(rootNode);

            #endregion Rating service selection body

            sequrityRequest = sequrityXmlDocument.InnerXml;
            return xmlDocument.InnerXml;
        }

        private async Task<HttpResponseMessage> ProcessRequest(string sequrityXml, string shippingXml)
        {
            using (var client = new HttpClient())
            {
                client.Timeout = new TimeSpan(0, 0, 0, 30);
                return await client.PostAsync(ServiceURL, new StringContent(sequrityXml + shippingXml, new MediaTypeHeaderValue("text/xml")));
            }
        }

        private PriceRaw ProcessResponse(XmlDocument xmlDocument, Order order)
        {
            var statusNode = xmlDocument.SelectSingleNode("/RatingServiceSelectionResponse/Response/ResponseStatusCode");
            if (statusNode?.InnerText == "0")
            {
                XmlNode? errNode = xmlDocument.SelectSingleNode("/RatingServiceSelectionResponse/Response/Error");

                throw new Exception(errNode?.SelectSingleNode("ErrorDescription")?.InnerText);
            }
            else
            {
                var rateNode = xmlDocument.SelectSingleNode("/RatingServiceSelectionResponse/RatedShipment/TotalCharges");
                string currencyCode = rateNode?.SelectSingleNode("CurrencyCode")?.InnerText ?? "";
                double rate = Converter.ToDouble(rateNode?.SelectSingleNode("MonetaryValue")?.InnerText);

                var warningNodes = xmlDocument.SelectNodes("RatingServiceSelectionResponse/RatedShipment/RatedShipmentWarning");
                if (warningNodes != null)
                {
                    foreach (XmlNode warningNode in warningNodes)
                    {
                        order.ShippingProviderWarnings.Add(warningNode.InnerText);
                    }
                }

                return new PriceRaw(rate, Services.Currencies.GetCurrency(currencyCode));
            }
        }

        #endregion

        #region GetOptions

        /// <summary>
        /// Retrieves options
        /// </summary>
        /// <param name="parameterName">Delivery Service, Pickup Type, Container Type, Customer Classification, Dimensions Unit, Weight Unit or Origination State</param>
        /// <returns>HashTable with options having index and description data</returns>
        public IEnumerable<ParameterOption> GetParameterOptions(string parameterName)
        {
            var options = new List<ParameterOption>();

            switch (parameterName)
            {
                case "Delivery Service":
                    options.Add(new("Next Day Air", "01"));
                    options.Add(new("Next Day Air Early AM", "14"));
                    options.Add(new("Next Day Air Saver", "13"));
                    options.Add(new("2nd Day Air AM", "59"));
                    options.Add(new("2nd Day Air", "02"));
                    options.Add(new("3 Day Select", "12"));
                    options.Add(new("Ground (1-5 Business Days)", "03"));
                    break;

                case "Pickup Type":
                    options.Add(new("RDP - Daily Pickup", "01"));
                    options.Add(new("CC - Customer Counter", "03"));
                    options.Add(new("OTP - One Time Pickup", "06"));
                    options.Add(new("OCA - On Call Air", "07"));
                    options.Add(new("LC - Letter Center", "19"));
                    options.Add(new("ASC – Air Service Center", "20"));
                    break;

                case "Container Type":
                    options.Add(new("UNKNOWN", "00"));
                    options.Add(new("UPS Letter", "01"));
                    options.Add(new("Package", "02"));
                    options.Add(new("Tube", "03"));
                    options.Add(new("Pak", "04"));
                    options.Add(new("Express Box", "21"));
                    options.Add(new("25KG Box", "24"));
                    options.Add(new("10KG Box", "25"));
                    options.Add(new("Pallet", "30"));
                    options.Add(new("Small Express Box", "2a"));
                    options.Add(new("Medium Express Box", "2b"));
                    options.Add(new("Large Express Box", "2c"));
                    break;

                case "Customer Classification":
                    options.Add(new("Rates Associated with Shipper Number", "00"));
                    options.Add(new("Daily Rates", "01"));
                    options.Add(new("Retail Rates", "04"));
                    options.Add(new("Standard List Rates", "53"));
                    break;

                case "Dimensions Unit":
                    options.Add(new("Inches", "IN"));
                    options.Add(new("Centimeters", "CM"));
                    break;

                case "Weight Unit":
                    options.Add(new("Pounds", "LBS"));
                    options.Add(new("Kilograms", "KGS"));
                    break;

                case "Origination State":
                    options.Add(new("Alabama", "AL"));
                    options.Add(new("Alaska", "AK"));
                    options.Add(new("Arizona", "AZ"));
                    options.Add(new("Arkansas", "AR"));
                    options.Add(new("California", "CA"));
                    options.Add(new("Colorado", "CO"));
                    options.Add(new("Connecticut", "CT"));
                    options.Add(new("Delaware", "DE"));
                    options.Add(new("District of Columbia", "DC"));
                    options.Add(new("Florida", "FL"));
                    options.Add(new("Georgia", "GA"));
                    options.Add(new("Hawaii", "HI"));
                    options.Add(new("Idaho", "ID"));
                    options.Add(new("Illinois", "IL"));
                    options.Add(new("Indiana", "IN"));
                    options.Add(new("Iowa", "IA"));
                    options.Add(new("Kansas", "KS"));
                    options.Add(new("Kentucky", "KY"));
                    options.Add(new("Louisiana", "LA"));
                    options.Add(new("Maine", "ME"));
                    options.Add(new("Maryland", "MD"));
                    options.Add(new("Massachusetts", "MA"));
                    options.Add(new("Michigan", "MI"));
                    options.Add(new("Minnesota", "MN"));
                    options.Add(new("Mississippi", "MS"));
                    options.Add(new("Missouri", "MO"));
                    options.Add(new("Montana", "MT"));
                    options.Add(new("Nebraska", "NE"));
                    options.Add(new("Nevada", "NV"));
                    options.Add(new("New Hampshire", "NH"));
                    options.Add(new("New Jersey", "NJ"));
                    options.Add(new("New Mexico", "NM"));
                    options.Add(new("New York", "NY"));
                    options.Add(new("North Carolina", "NC"));
                    options.Add(new("North Dakota", "ND"));
                    options.Add(new("Ohio", "OH"));
                    options.Add(new("Oklahoma", "OK"));
                    options.Add(new("Oregon", "OR"));
                    options.Add(new("Pennsylvania", "PA"));
                    options.Add(new("Rhode Island", "RI"));
                    options.Add(new("South Carolina", "SC"));
                    options.Add(new("South Dakota", "SD"));
                    options.Add(new("Tennessee", "TN"));
                    options.Add(new("Texas", "TX"));
                    options.Add(new("Utah", "UT"));
                    options.Add(new("Vermont", "VT"));
                    options.Add(new("Virginia", "VA"));
                    options.Add(new("Washington", "WA"));
                    options.Add(new("West Virginia", "WV"));
                    options.Add(new("Wisconsin", "WI"));
                    options.Add(new("Wyoming", "WY"));

                    break;
            }

            return options;
        }

        #endregion
    }
}
