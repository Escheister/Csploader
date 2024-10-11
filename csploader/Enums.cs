namespace ProtocolEnums
{
    enum ProtocolReply : int
    {
        Ok = 0, 
        Null = 1,
        WCrc = 2,
        WSig = 3,
        WCmd = 4, 
        WDat = 5,
    }
    enum CmdOutput : ushort // Request
    {
        NONE = 0x0000,
        ROUTING_THROUGH = 0x0210,
        ROUTING_PROG = 0x0211,
        START_BOOTLOADER = 0x1000, 
        LOAD_DATA_PAGE = 0x1003, 
        UPDATE_DATA_PAGE = 0x1005, 
        STOP_BOOTLOADER = 0x1007,
    }
    enum CmdInput : ushort  // Reply
    {
        NONE = 0x0000,
        ROUTING_THROUGH = 0x8210, 
        ROUTING_PROG = 0x8211,
        START_BOOTLOADER = 0x9002, 
        LOAD_DATA_PAGE = 0x9004,
        UPDATE_DATA_PAGE = 0x9006, 
        STOP_BOOTLOADER = 0x9008,
    }
    enum FileCheck : int 
    { 
        Ok = 0, 
        BadFile = 1, 
        CmdError = 2, 
        CrcError = 3 
    }
    enum RecordType : int
    {
        /// <summary>
        /// 0x00 - Data Record
        /// </summary>
        Rec = 0,
        /// <summary>
        /// 0x01 - End of File Record
        /// </summary>
        EndRec = 1,
        /// <summary>
        /// 0x02 - Extended Segment Address Record
        /// </summary>
        ESAR = 2,
        /// <summary>
        /// 0x03 - Start Segment Address Record
        /// </summary>
        SSAR = 3,
        /// <summary>
        /// 0x04 - Extended Linear Address Record
        /// </summary>
        ELAR = 4,
        /// <summary>
        /// 0x05 - Start Linear Address Record
        /// </summary>
        SLAR = 5
    }
}
