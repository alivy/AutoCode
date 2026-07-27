using AutoCode.Model.InterfaceAttribute;

namespace APP.WebAPI.Services
{
    [AutoInterface]
    public class BookingService : IBookingService, IScoped
    {
        /// <summary>
        /// 执行Booking
        /// </summary>
        /// <returns></returns>
        public string ExcuteBooking()
        {
            return "执行Booking完成";
        }
    }


    [AutoInterface]
    public class BookingServicePxoy : IBookingService, IScoped
    {
        /// <summary>
        /// 执行Booking
        /// </summary>
        /// <returns></returns>
        public string ExcuteBooking()
        {
            return "执行Booking完成";
        }

        /// <summary>
        /// 执行Booking
        /// </summary>
        /// <returns></returns>
        public string ExcuteBookingQuery()
        {
            return "执行Booking完成";
        }
    }







    [AutoInterface]
    public class BookingQeruyService : IBookingQeruyService, IScoped
    {
        /// <summary>
        /// 执行Booking
        /// </summary>
        /// <returns></returns>
        public string ExcuteBookingQuery()
        {
            return "执行Booking完成";
        }

        /// <summary>
        /// 执行Booking
        /// </summary>
        /// <returns></returns>
        public string ExcuteBooking()
        {
            return "执行Booking完成";
        }
    }
}
