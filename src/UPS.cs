using Dynamicweb.Core;
using Dynamicweb.Ecommerce.Cart;
using Dynamicweb.Ecommerce.Orders;
using Dynamicweb.Ecommerce.Prices;
using Dynamicweb.Extensibility.AddIns;
using Dynamicweb.Extensibility.Editors;
using System;
using System.Collections;
using System.Collections.Generic;
using System.IO;
using System.Net;
using System.Xml;

namespace Dynamicweb.Ecommerce.ShippingProviders.UPS
{
    /// <summary>
    /// UPS Shipping Service
    /// </summary>
    [AddInName("UPS"), AddInDescription("UPS Delivery provider")]
    public class UPS : ShippingProvider, IDropDownOptions
    {
        #region Parameters

        [AddInParameter("User ID"), AddInParameterEditor(typeof(TextParameterEditor), "size=80")]
        public string UserID { get; set; }

        [AddInParameter("Password"), AddInParameterEditor(typeof(TextParameterEditor), "size=80")]
        public string Password { get; set; }

        [AddInParameter("Access Key"), AddInParameterEditor(typeof(TextParameterEditor), "size=80")]
        public string AccessKey { get; set; }

        [AddInParameter("Rate service URL"), AddInParameterEditor(typeof(TextParameterEditor), "size=80")]
        public string ServiceURL { get; set; }

        [AddInParameter("Delivery Service"), AddInParameterEditor(typeof(DropDownParameterEditor), "SortBy=Value")]
        public string DeliveryService { get; set; }

        [AddInParameter("Pickup Type"), AddInParameterEditor(typeof(DropDownParameterEditor), "SortBy=Value")]
        public string PickupType { get; set; }

        [AddInParameter("Container Type"), AddInParameterEditor(typeof(DropDownParameterEditor), "SortBy=Value")]
        public string ContainerType { get; set; }

        [AddInParameter("Customer Classification"), AddInParameterEditor(typeof(DropDownParameterEditor), "SortBy=Value")]
        public string CustomerClassification { get; set; }

        [AddInParameter("UPS Account Number"), AddInParameterEditor(typeof(TextParameterEditor), "")]
        public string ShipperNumber { get; set; }

        [AddInParameter("Group by manufacturer"), AddInParameterEditor(typeof(YesNoParameterEditor), "")]
        public bool GroupByManufacturer { get; set; }

        [AddInParameter("Number Of Products Per Package"), AddInParameterEditor(typeof(TextParameterEditor), "")]
        public string NumberOfProductsPerPackage { get; set; }

        [AddInParameter("Dimensions Unit"), AddInParameterEditor(typeof(DropDownParameterEditor), "")]
        public string DimensionsUnitOfMeasurement { get; set; }

        [AddInParameter("Default Length"), AddInParameterEditor(typeof(TextParameterEditor), "")]
        public string Length { get; set; }

        [AddInParameter("Default Width"), AddInParameterEditor(typeof(TextParameterEditor), "")]
        public string Width { get; set; }

        [AddInParameter("Default Height"), AddInParameterEditor(typeof(TextParameterEditor), "")]
        public string Height { get; set; }

        [AddInParameter("Weight Unit"), AddInParameterEditor(typeof(DropDownParameterEditor), "")]
        public string WeightUnitOfMeasurement { get; set; }

        [AddInParameter("Company Name"), AddInParameterEditor(typeof(TextParameterEditor), "")]
        public string CompanyName { get; set; }

        [AddInParameter("Attention Name"), AddInParameterEditor(typeof(TextParameterEditor), "")]
        public string AttentionName { get; set; }

        [AddInParameter("Phone Number"), AddInParameterEditor(typeof(TextParameterEditor), "")]
        public string PhoneNumber { get; set; }

        [AddInParameter("Fax Number"), AddInParameterEditor(typeof(TextParameterEditor), "")]
        public string FaxNumber { get; set; }

        [AddInParameter("Origination Street Address"), AddInParameterEditor(typeof(TextParameterEditor), "")]
        public string StreetAddress { get; set; }

        [AddInParameter("Origination Street Address 2"), AddInParameterEditor(typeof(TextParameterEditor), "")]
        public string StreetAddress2 { get; set; }

        [AddInParameter("Origination City"), AddInParameterEditor(typeof(TextParameterEditor), "")]
        public string City { get; set; }

        [AddInParameter("Origination State"), AddInParameterEditor(typeof(DropDownParameterEditor), "SortBy=Value")]
        public string StateProvinceCode { get; set; }

        [AddInParameter("Origination Zip Code"), AddInParameterEditor(typeof(TextParameterEditor), "")]
        public string PostalCode { get; set; }

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
        public override PriceRaw CalculateShippingFee(Order order)
        {
            var shippingXml = string.Empty;
            PriceRaw rate = null;
            order.ShippingProviderErrors.Clear();
            order.ShippingProviderWarnings.Clear();

            try
            {
                if (IsRequestParametersCorrect(order))
                {
                    var sequrityXmlString = string.Empty;
                    shippingXml = CreateRequest(order, ref sequrityXmlString);

                    var rateRequest = ShippingProviderHelper.CheckIsRateRequestCached(ShippingID, shippingXml);
                    if (rateRequest.Rate > 0 || ShippingProviderHelper.IsThisShippingRequestWasProcessed(ShippingID))
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
                        using (var webResponse = ProcessRequest(sequrityXmlString, shippingXml))
                        {
                            xmlDocument.Load(webResponse.GetResponseStream());
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
            ShippingProviderHelper.SetShippingRequestIsProcessed(ShippingID);

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

        private WebResponse ProcessRequest(string sequrityXml, string shippingXml)
        {
            var webRequest = WebRequest.Create(ServiceURL);
            webRequest.Method = "POST";
            webRequest.ContentType = "text/xml";
            webRequest.Proxy = null;

            var writer = new StreamWriter(webRequest.GetRequestStream());
            writer.WriteLine(sequrityXml);
            writer.WriteLine(shippingXml);
            writer.Close();

            webRequest.Timeout = 30000;
            return webRequest.GetResponse();
        }

        private PriceRaw ProcessResponse(XmlDocument xmlDocument, Order order)
        {
            var statusNode = xmlDocument.SelectSingleNode("/RatingServiceSelectionResponse/Response/ResponseStatusCode");
            if (statusNode.InnerText == "0")
            {
                XmlNode errNode = xmlDocument.SelectSingleNode("/RatingServiceSelectionResponse/Response/Error");

                throw new Exception(errNode.SelectSingleNode("ErrorDescription").InnerText);
            }
            else
            {
                var rateNode = xmlDocument.SelectSingleNode("/RatingServiceSelectionResponse/RatedShipment/TotalCharges");
                string currencyCode = rateNode.SelectSingleNode("CurrencyCode").InnerText;
                double rate = Converter.ToDouble(rateNode.SelectSingleNode("MonetaryValue").InnerText);

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
        /// <param name="optionName">Delivery Service, Pickup Type, Container Type, Customer Classification, Dimensions Unit, Weight Unit or Origination State</param>
        /// <returns>HashTable with options having index and description data</returns>
        public Hashtable GetOptions(string optionName)
        {
            var options = new Hashtable();

            switch (optionName)
            {
                case "Delivery Service":
                    options.Add("01", "Next Day Air");
                    options.Add("14", "Next Day Air Early AM");
                    options.Add("13", "Next Day Air Saver");
                    options.Add("59", "2nd Day Air AM");
                    options.Add("02", "2nd Day Air");
                    options.Add("12", "3 Day Select");
                    options.Add("03", "Ground (1-5 Business Days)");
                    break;

                case "Pickup Type":
                    options.Add("01", "RDP - Daily Pickup");
                    options.Add("03", "CC - Customer Counter");
                    options.Add("06", "OTP - One Time Pickup");
                    options.Add("07", "OCA - On Call Air");
                    options.Add("19", "LC - Letter Center");
                    options.Add("20", "ASC – Air Service Center");
                    break;

                case "Container Type":
                    options.Add("00", "UNKNOWN");
                    options.Add("01", "UPS Letter");
                    options.Add("02", "Package");
                    options.Add("03", "Tube");
                    options.Add("04", "Pak");
                    options.Add("21", "Express Box");
                    options.Add("24", "25KG Box");
                    options.Add("25", "10KG Box");
                    options.Add("30", "Pallet");
                    options.Add("2a", "Small Express Box");
                    options.Add("2b", "Medium Express Box");
                    options.Add("2c", "Large Express Box");
                    break;

                case "Customer Classification":
                    options.Add("00", "Rates Associated with Shipper Number");
                    options.Add("01", "Daily Rates");
                    options.Add("04", "Retail Rates");
                    options.Add("53", "Standard List Rates");
                    break;

                case "Dimensions Unit":
                    options.Add("IN", "Inches");
                    options.Add("CM", "Centimeters");
                    break;

                case "Weight Unit":
                    options.Add("LBS", "Pounds");
                    options.Add("KGS", "Kilograms");
                    break;

                case "Origination State":
                    options.Add("AL", "Alabama");
                    options.Add("AK", "Alaska");
                    options.Add("AZ", "Arizona");
                    options.Add("AR", "Arkansas");
                    options.Add("CA", "California");
                    options.Add("CO", "Colorado");
                    options.Add("CT", "Connecticut");
                    options.Add("DE", "Delaware");
                    options.Add("DC", "District of Columbia");
                    options.Add("FL", "Florida");
                    options.Add("GA", "Georgia");
                    options.Add("HI", "Hawaii");
                    options.Add("ID", "Idaho");
                    options.Add("IL", "Illinois");
                    options.Add("IN", "Indiana");
                    options.Add("IA", "Iowa");
                    options.Add("KS", "Kansas");
                    options.Add("KY", "Kentucky");
                    options.Add("LA", "Louisiana");
                    options.Add("ME", "Maine");
                    options.Add("MD", "Maryland");
                    options.Add("MA", "Massachusetts");
                    options.Add("MI", "Michigan");
                    options.Add("MN", "Minnesota");
                    options.Add("MS", "Mississippi");
                    options.Add("MO", "Missouri");
                    options.Add("MT", "Montana");
                    options.Add("NE", "Nebraska");
                    options.Add("NV", "Nevada");
                    options.Add("NH", "New Hampshire");
                    options.Add("NJ", "New Jersey");
                    options.Add("NM", "New Mexico");
                    options.Add("NY", "New York");
                    options.Add("NC", "North Carolina");
                    options.Add("ND", "North Dakota");
                    options.Add("OH", "Ohio");
                    options.Add("OK", "Oklahoma");
                    options.Add("OR", "Oregon");
                    options.Add("PA", "Pennsylvania");
                    options.Add("RI", "Rhode Island");
                    options.Add("SC", "South Carolina");
                    options.Add("SD", "South Dakota");
                    options.Add("TN", "Tennessee");
                    options.Add("TX", "Texas");
                    options.Add("UT", "Utah");
                    options.Add("VT", "Vermont");
                    options.Add("VA", "Virginia");
                    options.Add("WA", "Washington");
                    options.Add("WV", "West Virginia");
                    options.Add("WI", "Wisconsin");
                    options.Add("WY", "Wyoming");

                    break;
            }

            return options;
        }

        #endregion
    }
}
