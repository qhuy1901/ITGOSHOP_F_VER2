﻿using Microsoft.AspNetCore.Mvc;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using ITGoShop_F_Ver2.Models;
using Microsoft.AspNetCore.Http;
using MyCardSession.Helpers;
using System.Globalization;

namespace ITGoShop_F_Ver2.Controllers
{
    public class CheckoutController : Controller
    {
        public IActionResult Index()
        {
            int customerId = Convert.ToInt32(HttpContext.Session.GetInt32("customerId"));
            if (customerId != 0) // Nếu customer đã đăng nhập
            {
                /*===Cái này để load layout ===*/
                ITGoShopContext context = HttpContext.RequestServices.GetService(typeof(ITGoShop_F_Ver2.Models.ITGoShopContext)) as ITGoShopContext;
                var linqContext = new ITGoShopLINQContext();
                ViewBag.AllCategory = linqContext.getAllCategory();
                ViewBag.AllBrand = linqContext.getAllBrand();
                ViewBag.AllSubBrand = linqContext.getAllSubBrand();
                /*======*/

                ViewBag.DefaultShippingAddress = context.getDefaultShippingAddress(customerId);
                ViewBag.AllShipMethod = linqContext.getShipMethodToCheckout();
                var cart = SessionHelper.GetObjectFromJson<List<CartItem>>(HttpContext.Session, "cart");
                ViewBag.cart = cart;
                if (cart == null)
                {
                    ViewBag.total = 0;
                    ViewBag.numberItem = 0;
                }
                else
                {
                    ViewBag.numberItem = cart.Sum(item => item.Quantity);
                    ViewBag.total = cart.Sum(item => item.Product.Price * item.Quantity);
                }
                ViewBag.AllThanhPho = linqContext.getAllThanhPho();
                ViewBag.AllQuanHuyen = linqContext.getAllQuanHuyen();
                ViewBag.AllXaPhuong = linqContext.getAllXaPhuong();
                return View();
            }
            return RedirectToAction("login_to_checkout");
        }
        public IActionResult login_to_checkout(string message)
        {
            if (!string.IsNullOrEmpty(ViewBag.message))
            {
                ViewBag.message = message;
            }
            return View();
        }

        public IActionResult checkout_after_login(User userInput)
        {
            ITGoShopContext context = HttpContext.RequestServices.GetService(typeof(ITGoShop_F_Ver2.Models.ITGoShopContext)) as ITGoShopContext;
            User userInfo = context.getUserInfo(userInput.Email, userInput.Password, 0);
            if (userInfo != null)
            {
                HttpContext.Session.SetInt32("customerId", userInfo.UserId);
                HttpContext.Session.SetString("customerLastName", userInfo.LastName);
                HttpContext.Session.SetString("customerFirstName", userInfo.FirstName);
                HttpContext.Session.SetString("customerImage", userInfo.UserImage);
                HttpContext.Session.SetString("customerEmail", userInfo.Email);

                var LINQContext = new ITGoShopLINQContext();
                LoginHistory login = new LoginHistory(userInfo.UserId, DateTime.Now, DateTime.Now);
                LINQContext.updateLoginHistory(login);
                // Update last login
                context.updateLastLogin(userInfo.UserId);
                return RedirectToAction("Index");
            }
            return RedirectToAction("login_to_checkout", new { message = "Mật khẩu hoặc tài khoản sai. Xin nhập lại!" });
        }

        public async Task<IActionResult> create_orderAsync(Order order)
        {
            // Lấy thông tin từ cart để thêm thông tin đơn hàng vào bảng ORDER
            var cart = SessionHelper.GetObjectFromJson<List<CartItem>>(HttpContext.Session, "cart");
            ITGoShopLINQContext linqContext = new ITGoShopLINQContext();
            ITGoShopContext context = HttpContext.RequestServices.GetService(typeof(ITGoShop_F_Ver2.Models.ITGoShopContext)) as ITGoShopContext;
            order.Total = order.ShipFee + cart.Sum(item => item.Product.Price * item.Quantity);
            order.UserId = Convert.ToInt32(HttpContext.Session.GetInt32("customerId"));
            order.DateUpdate = DateTime.Now;
            order.OrderDate = DateTime.Now;
            order.OrderStatus = "Đặt hàng thành công";
            order.PaymentStatus = "Chờ thanh toán";
            int numberOfProduct = cart.Sum(item => item.Quantity);
            order.Description = cart.First().Product.ProductName;
            if (numberOfProduct > 1)
                order.Description += " và " + (numberOfProduct - 1) + " sản phẩm khác";
            int orderId = linqContext.createOrder(order);

            // Thêm theo dõi đơn hàng
            linqContext.addOrderTracking(orderId, "Đặt hàng thành công");

            foreach (var item in cart)
            {
                // Thêm các thông tin vào bảng chi tiết hóa đơn
                OrderDetail orderDetail = new OrderDetail()
                {
                    OrderQuantity = item.Quantity,
                    ProductId = item.Product.ProductId,
                    UnitPrice = item.Product.Price,
                    OrderId = order.OrderId
                };
                linqContext.saveOrderDetail(orderDetail);

                Product productInfo = context.getProductInfo(item.Product.ProductId);

                //Update thông tin doanh thu lên bảng statistic
                Statistic statistic = linqContext.getStatistic(DateTime.Now);
                Statistic newStatisticInfo = new Statistic()
                {
                    StatisticDate = DateTime.Now,
                    Sales = (int)(item.Quantity * item.Product.Price),
                    Profit = (int)((item.Product.Price - item.Product.Cost) * item.Quantity)
                };
                if (statistic != null)
                {
                    // Nếu đã có dòng thống kê doanh thu cho hôm nay thì cập nhật dữ liệu
                    linqContext.updateStatistic(newStatisticInfo);
                }
                else
                {
                    // Nếu chưa có dòng thống kê cho hôm nay thì tạo mới
                    linqContext.addStatistic(newStatisticInfo);
                }

                // Trừ số lượng tồn kho
                linqContext.updateSoldProduct(productInfo.ProductId, item.Quantity);
            }
            cart.Clear();
            SessionHelper.SetObjectAsJson(HttpContext.Session, "cart", cart);

            // Gửi mail

            string customerFirstName = HttpContext.Session.GetString("customerFirstName");
            string customerLastName = HttpContext.Session.GetString("customerLastName");
            string customerEmail = HttpContext.Session.GetString("customerEmail");
            string mailContent = getMailContent(order, customerFirstName, customerLastName);
            await MailUtils.SendMailGoogleSmtp("itgoshop863@gmail.com", customerEmail, $"Chào {customerFirstName}, ITGoShop đã nhận được đơn hàng của bạn", mailContent,
                                          "itgoshop863@gmail.com", "Itgoshop");
            return RedirectToAction("order_detail", "Order", new { orderId = orderId });
        }
        public string getMailContent(Order orderInfo, string customerFirstName, string customerLastName)
        {
            CultureInfo cul = CultureInfo.GetCultureInfo("vi-VN");
            string ShipFee = orderInfo.ShipFee.ToString("#,###", cul.NumberFormat);
            string ToTal = orderInfo.Total.ToString("#,###", cul.NumberFormat);
            string EstimatedDeliveryTime = orderInfo.EstimatedDeliveryTime.ToString("dd-MM-yyyy");
            string OrderDate = orderInfo.OrderDate.ToString("HH:mm dd-MM-yyyy");
            ITGoShopContext context = HttpContext.RequestServices.GetService(typeof(ITGoShop_F_Ver2.Models.ITGoShopContext)) as ITGoShopContext;
            object shippingAddress = context.getShippingAddress(orderInfo.ShippingAddressId);

            string output = @$"<body>
                <div class='card' style='margin: 40px 100px;'>
                        <div class='card-body' style='font-size:16px'>
                            <h2>Cảm ơn quý khách {customerLastName} {customerFirstName} đã đặt hàng tại ITGoShop,</h2>
                            <p class='card-text'>ITGoShop rất vui thông báo đơn hàng #{orderInfo.OrderId} của quý khách đã được tiếp nhận và đang trong quá trình xử lý. ITGoShop sẽ thông báo đến quý khách ngay khi hàng chuẩn bị được giao.</p>
                            <p class='card-text' style='color:#77ACF1;'><b>THÔNG TIN ĐƠN HÀNG #{orderInfo.OrderId}</b>  (Thời gian đặt hàng: {OrderDate})</p>
                            <hr>
                            <p class='card-text'><b>Mô tả đơn hàng:</b> {orderInfo.Description}</p>
                            <p class= 'card-text'><b> Địa chỉ giao hàng:</b></p>
                            <p> Tên người nhận: {shippingAddress.GetType().GetProperty("ReceiverName").GetValue(shippingAddress, null)}</p>
                            <p> Địa chỉ: {shippingAddress.GetType().GetProperty("Address").GetValue(shippingAddress, null)}, {shippingAddress.GetType().GetProperty("XaPhuong").GetValue(shippingAddress, null)}, {shippingAddress.GetType().GetProperty("QuanHuyen").GetValue(shippingAddress, null)}, {shippingAddress.GetType().GetProperty("ThanhPho").GetValue(shippingAddress, null)}</p>
                            </p> Điện thoại: {shippingAddress.GetType().GetProperty("Phone").GetValue(shippingAddress, null)}</p>
                            <p class= 'card-text'><b> Phương thức thanh toán:</b> {orderInfo.ShipMethod}</p>
                            <p class= 'card-text'><b> Thời gian giao hàng dự kiến: </b> dự kiến giao hàng vào ngày {EstimatedDeliveryTime}</p>
                            <p class= 'card-text'><b> Phí vận chuyển: </b>{ShipFee} ₫</p>
                            <p class= 'card-text'><b> TỔNG TRỊ GIÁ ĐƠN HÀNG: </b><b style = 'color:red; font-size: 20px' >{ToTal} ₫</b></p>
                            <p class= 'card-text'> Trân trọng,</p>
                            <p class='card-text'> Đội ngũ ITGoShop.</p>
                            <p class= 'card-text'><i> Lưu ý: Với những đơn hàng thanh toán trả trước, xin vui lòng đảm bảo người nhận hàng đúng thông tin đã đăng kí trong đơn hàng, và chuẩn bị giấy tờ tùy thân để đơn vị giao nhận có thể xác thực thông tin khi giao hàng</i></p>
                        </div>
                  </div>
            </body>";
            return output;
        }
    }
}
