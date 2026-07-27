using APP.WebAPI.Services;
using Microsoft.AspNetCore.Mvc;

namespace APP.WebAPI.Controllers
{
    [ApiController]
    [Route("[controller]/[action]")]
    public class BookingController : ControllerBase
    {

        public IBookingService _bookingService;
        public BookingController(IBookingService bookingService)
        {
            _bookingService = bookingService;
        }
        /// <summary>
        /// 获取订单
        /// </summary>
        /// <returns></returns>
        [HttpPost]
        public string GetBooking()
        {
            return _bookingService.ExcuteBooking();
        }
    }
}
