﻿using System;
using System.Collections.Generic;
using System.Text;
using kOS.Safe.UserIO;

namespace kOS.UserIO
{
    /// <summary>
    /// A base class for terminal-specific mappings like vt100, xterm, etc.
    /// <br/>
    /// It encodes common terminal control codes in unicode, using some of the
    /// unicode characters that we'll never use, as pretend terminal escape codes.
    /// <br/>
    /// Subclasses of this need to implement a way to convert those unicode control
    /// codes into a stream of ascii chars, and visa versa, that do things using the
    /// terminal control codes for the specific model of terminal in question.
    /// </summary>
    public class TerminalUnicodeMapper
    {
        /// <summary>
        /// This should be used when trying to get the exact match for conditional checks:
        /// </summary>
        public TerminalType TerminalTypeID {get; protected set;}
        
        /// <summary>
        /// This is the type as a raw string, without any data tokenizing:
        /// </summary>
        public string TerminalTypeString {get; protected set;}
        
        protected readonly object lockAccess = new object(); // to ensure that multiple threads use the mapper atomicly, to avoid messing up it's state variables.
        
        // Note that it's essential that these remain private and not get changed to protected or public.
        // Some of the derived classes of this class also use the same identifier name for their own
        // fields that have a very similar purpose, and in those cases there actually needs to be two
        // separate instances of these fields living inside the object.  One instance tracks the state
        // of the base class's mapping algorithm, and the other tracks the state of the derived class's
        // mapping algorithm:
        private ExpectNextChar outputExpected = ExpectNextChar.NORMAL; // must stay private
        private int pendingWidth; // must stay private
        
        public TerminalUnicodeMapper(string typeString)
        {
            TerminalTypeString = typeString;
            TerminalTypeID = TerminalType.UNKNOWN;
        }

        public static TerminalType GuessTypeId(string typeString)
        {
            if (typeString.Substring(0,5).Equals("xterm", StringComparison.CurrentCultureIgnoreCase))
                return TerminalType.XTERM;
            else if (typeString.Substring(0,4).Equals("ansi", StringComparison.CurrentCultureIgnoreCase))
                return TerminalType.XTERM;
            // Add more cases here if more subclasses of this class are created later.
            else
                return TerminalType.UNKNOWN;
        }
        
        /// <summary>
        /// Construct an object of this type, or one of its derived subtypes, depending on
        /// the terminal type string passed in.
        /// </summary>
        /// <param name="typeString">terminal type id string to make a mapper for.</param>
        /// <returns>newly constructed mapper object of the proper type.</returns>
        public static TerminalUnicodeMapper TerminalMapperFactory(string typeString)
        {
            TerminalType termType = GuessTypeId(typeString);
            switch (termType)
            {
                case TerminalType.XTERM:
                    return new TerminalXtermMapper(typeString);
                case TerminalType.ANSI:
                    return new TerminalAnsiMapper(typeString);
                default:
                    return new TerminalUnicodeMapper(typeString);
            }
        }
        
        /// <summary>
        /// Map the unicode chars (and the fake control codes we made) into bytes.
        /// In this base class, all it does is just mostly passthru things as-is with no
        /// conversions.
        /// Subclasses of this should perform their own manipulations, then fallthrough
        /// to this base class inmplementation at the bottom, to allow chains of
        /// subclasses to all operate on the data.
        /// </summary>
        /// <param name="ch">unicode char</param>
        /// <returns>raw byte stream to send to the terminal</returns>
        public virtual char[] OutputConvert(string str)
        {
            System.Console.WriteLine("eraseme: TerminalUnicodeMapper: Passed string = "+str);
            StringBuilder sb = new StringBuilder();

            for (int index = 0 ; index < str.Length ; ++index)
            {
                switch (outputExpected)
                {
                    case ExpectNextChar.RESIZEWIDTH:
                        pendingWidth = (int)(str[index]);
                        outputExpected = ExpectNextChar.RESIZEHEIGHT;
                        System.Console.WriteLine("eraseme: TerminalUnicodeMapper: found RESIZEWIDTH char. Value = "+pendingWidth);
                        break;
                    case ExpectNextChar.RESIZEHEIGHT:
                        int height = (int)(str[index]);
                        System.Console.WriteLine("eraseme: TerminalUnicodeMapper: found RESIZEHEIGHT char. Value = "+height);
                        sb.Append("{Please resize to " + pendingWidth + "x" + height + "}"); // By default, assume the terminal has no such control code, but this can be overridden.
                        System.Console.WriteLine("eraseme: TerminalUnicodeMapper: sending string = " + sb.ToString());
                        outputExpected = ExpectNextChar.NORMAL;
                        break;
                    case ExpectNextChar.INTITLE:
                        // Default behavior: Ignore all content until the title ender.  Assume most terminals don't know how to do this.
                        if (str[index] == (char)UnicodeCommand.TITLEEND)
                            outputExpected = ExpectNextChar.NORMAL;
                        break;
                    default:
                        switch (str[index])
                        {
                            case (char)(UnicodeCommand.RESIZESCREEN):
                                outputExpected = ExpectNextChar.RESIZEWIDTH;
                                System.Console.WriteLine("eraseme: TerminalUnicodeMapper: found RESIZESCREEN.");
                                break;
                            case (char)(UnicodeCommand.TITLEBEGIN):
                                outputExpected = ExpectNextChar.INTITLE;
                                break;
                            case (char)(UnicodeCommand.STARTNEXTLINE):
                                sb.Append("\r\n");
                                break;
                            case (char)(UnicodeCommand.LINEFEEDKEEPCOL):
                                sb.Append("\n");
                                break;
                            case (char)(UnicodeCommand.GOTOLEFTEDGE):
                                sb.Append("\r");
                                break;
                            default: 
                                sb.Append(str[index]); // default passhtrough
                                break;
                        }
                        break;
                }
            }
            return sb.ToString().ToCharArray();
        }

        /// <summary>
        /// Map the bytes containing the terminal's own control codes into our
        /// unicode system with its fake control codes.  In this base class, all it
        /// does it just mostly passthru things as-is with no conversion.
        /// Subclasses of this should perform their own manipulations, then fallthrough
        /// to this base class implementation at the bottom, to allow chains of
        /// subclasses to all operate on the data.
        /// </summary>
        /// <param name="inChars">chars in the terminal's way of thinking</param>
        /// <returns>chars in our unicode way of thinking</returns>
        public virtual string InputConvert(char[] inChars)
        {
            List<char> outChars = new List<char>();
            
            char prevCh = '\0'; // dummy for first pass
            
            foreach (char ch in inChars)
            {
                switch (ch)
                {
                    case (char)0x08: // control-H, the ASCII backspace
                        outChars.Add((char)UnicodeCommand.DELETELEFT);
                        break;
                    case (char)0x7f: // ascii 127 = the delete character
                        outChars.Add((char)UnicodeCommand.DELETERIGHT);
                        break;
                    case '\r':
                        if (prevCh == '\n') // A \r after a \n should be ignored - we already got it from the \n:
                            outChars.Add(ch); // passthrough as-is.
                        else
                            outChars.Add((char)UnicodeCommand.STARTNEXTLINE);
                        break;
                    case '\n':
                        if (prevCh == '\r') // A \n after a \r should be ignored - we already got it from the \r:
                            outChars.Add(ch); // passthrough as-is.
                        else
                            outChars.Add((char)UnicodeCommand.STARTNEXTLINE);
                        break;
                    case (char)0x0c: // control-L.
                    case (char)0x12: // control-R.
                        outChars.Add((char)UnicodeCommand.REQUESTREPAINT);
                        break;
                    case (char)0x03: // Control-C. map to BREAK just as if the telnet client had sent a hard break in the protocol.
                        outChars.Add((char)UnicodeCommand.BREAK);
                        break;
                    default:
                        outChars.Add(ch); // dummy passthrough
                        break;
                }
                prevCh = ch;
            }
            return new string(outChars.ToArray());
        }
    }

    /// <summary>tokenization of the many different strings that might be returned as ID's from telnet clients</summary>    
    public enum TerminalType
    {
        UNKNOWN,
        XTERM,
        ANSI // Add more values here if more subclasses of this class are created later.
    }
}
