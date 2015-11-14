﻿// Enums.NET
// Copyright 2015 Tyler Brinkley. All rights reserved.
//
// Licensed under the Apache License, Version 2.0 (the "License");
// you may not use this file except in compliance with the License.
// You may obtain a copy of the License at
//
//	  http://www.apache.org/licenses/LICENSE-2.0
//
// Unless required by applicable law or agreed to in writing, software
// distributed under the License is distributed on an "AS IS" BASIS,
// WITHOUT WARRANTIES OR CONDITIONS OF ANY KIND, either express or implied.
// See the License for the specific language governing permissions and
// limitations under the License.

using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.Diagnostics;
using System.Diagnostics.CodeAnalysis;
using System.Globalization;
using System.Linq;
using System.Linq.Expressions;
using System.Reflection;
using System.Runtime.Serialization;

namespace EnumsNET
{
	internal class EnumsCache<TEnum> : IEnumsCache
	{
		#region Fields
		public static readonly bool IsFlagEnum;

		public static readonly bool IsContiguous;

		public static readonly TEnum AllFlags;

		internal static readonly TypeCode UnderlyingTypeCode;

		public static readonly Func<TEnum, TEnum, bool> Equal;

		[SuppressMessage("", "CS0108", Justification = "It's a static field")]
		public static readonly Func<TEnum, int> GetHashCode;

		// The main collection of values, names, and attributes with ~O(1) retrieval on name or value
		// If constant contains a DescriptionAttribute it will be the first in the attribute array
		private static readonly OrderedBiDirectionalDictionary<TEnum, NameAndAttributes> _valueMap;

		// Duplicate values are stored here with a key of the constant's name, is null if no duplicates
		private static readonly Dictionary<string, ValueAndAttributes<TEnum>> _duplicateValues;

		private static readonly TEnum _maxDefined;

		private static readonly TEnum _minDefined;

		private static readonly Func<TEnum, TEnum, TEnum> _and;

		private static readonly Func<TEnum, TEnum, TEnum> _or;

		private static readonly Func<TEnum, TEnum, TEnum> _xor;

		private static readonly Func<TEnum, TEnum, bool> _greaterThan;

		// Enables case insensitive parsing, lazily instantiated to reduce memory usage if not going to use this feature, is thread-safe
		private static readonly Lazy<Dictionary<string, string>> _ignoreCaseSet = new Lazy<Dictionary<string, string>>(InitializeIgnoreCaseSet);

		// Enables parsing by description, lazily instantiated to reduce memory usage if not going to use this feature, is thread-safe
		private static readonly Lazy<Dictionary<string, string>> _descriptionNameMap = new Lazy<Dictionary<string, string>>(InitializeDescriptionNameMap);

		// Enables case insensitive parsing by description, lazily instantiated to reduce memory usage if not going to use this feature, is thread-safe
		private static readonly Lazy<Dictionary<string, string>> _descriptionIgnoreCase = new Lazy<Dictionary<string, string>>(InitializeDescriptionIgnoreCase);

		// Enables parsing by EnumMemberValue, lazily instantiated to reduce memory usage if not going to use this feature, is thread-safe
		private static readonly Lazy<Dictionary<string, string>> _enumMemberValues = new Lazy<Dictionary<string, string>>(InitializeEnumMemberValues);

		// Enables case insensitive parsing by EnumMemberValue, lazily instantiated to reduce memory usage if not going to use this feature, is thread-safe
		private static readonly Lazy<Dictionary<string, string>> _enumMemberValuesIgnoreCase = new Lazy<Dictionary<string, string>>(InitializeEnumMemberValuesIgnoreCase);

		private static Dictionary<string, string> InitializeIgnoreCaseSet()
		{
			var ignoreCaseSet = new Dictionary<string, string>(GetDefinedCount(false), StringComparer.OrdinalIgnoreCase);
			foreach (var nameAndAttributes in _valueMap.SecondItems)
			{
				ignoreCaseSet.Add(nameAndAttributes.Name, nameAndAttributes.Name);
			}
			if (_duplicateValues != null)
			{
				foreach (var name in _duplicateValues.Keys)
				{
					ignoreCaseSet.Add(name, name);
				}
			}
			return ignoreCaseSet;
		}

		private static Dictionary<string, string> InitializeDescriptionNameMap()
		{
			var descriptionNameMap = new Dictionary<string, string>();
			foreach (var pair in _valueMap)
			{
				var description = new InternalEnumMemberInfo<TEnum>(pair).Description;
				if (description != null && !descriptionNameMap.ContainsKey(description))
				{
					descriptionNameMap.Add(description, pair.Second.Name);
				}
			}
			if (_duplicateValues != null)
			{
				foreach (var pair in _duplicateValues)
				{
					var description = new InternalEnumMemberInfo<TEnum>(pair).Description;
					if (description != null && !descriptionNameMap.ContainsKey(description))
					{
						descriptionNameMap.Add(description, pair.Key);
					}
				}
			}

			// Reduces memory usage
			return new Dictionary<string, string>(descriptionNameMap);
		}

		private static Dictionary<string, string> InitializeDescriptionIgnoreCase()
		{
			var descriptionIgnoreCase = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
			foreach (var pair in _descriptionNameMap.Value)
			{
				if (!descriptionIgnoreCase.ContainsKey(pair.Key))
				{
					descriptionIgnoreCase.Add(pair.Key, pair.Value);
				}
			}

			// Reduces memory usage
			return new Dictionary<string, string>(descriptionIgnoreCase, StringComparer.OrdinalIgnoreCase);
		}

		private static Dictionary<string, string> InitializeEnumMemberValues()
		{
			var enumMemberValues = new Dictionary<string, string>();
			foreach (var pair in _valueMap)
			{
				var enumMemberValue = new InternalEnumMemberInfo<TEnum>(pair).EnumMemberValue;
				if (enumMemberValue != null && !enumMemberValues.ContainsKey(enumMemberValue))
				{
					enumMemberValues.Add(enumMemberValue, pair.Second.Name);
				}
			}
			if (_duplicateValues != null)
			{
				foreach (var pair in _duplicateValues)
				{
					var enumMemberValue = new InternalEnumMemberInfo<TEnum>(pair).EnumMemberValue;
					if (enumMemberValue != null && !enumMemberValues.ContainsKey(enumMemberValue))
					{
						enumMemberValues.Add(enumMemberValue, pair.Key);
					}
				}
			}

			// Reduces memory usage
			return new Dictionary<string, string>(enumMemberValues);
		}

		private static Dictionary<string, string> InitializeEnumMemberValuesIgnoreCase()
		{
			var enumMemberValuesIgnoreCase = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
			foreach (var pair in _enumMemberValues.Value)
			{
				if (!enumMemberValuesIgnoreCase.ContainsKey(pair.Key))
				{
					enumMemberValuesIgnoreCase.Add(pair.Key, pair.Value);
				}
			}

			// Reduces memory usage
			return new Dictionary<string, string>(enumMemberValuesIgnoreCase, StringComparer.OrdinalIgnoreCase);
		}
		#endregion

		#region Properties
		public static Type UnderlyingType
		{
			get
			{
				switch (UnderlyingTypeCode)
				{
					case TypeCode.Int32:
						return typeof(int);
					case TypeCode.UInt32:
						return typeof(uint);
					case TypeCode.SByte:
						return typeof(sbyte);
					case TypeCode.Byte:
						return typeof(byte);
					case TypeCode.Int16:
						return typeof(short);
					case TypeCode.UInt16:
						return typeof(ushort);
					case TypeCode.Int64:
						return typeof(long);
					case TypeCode.UInt64:
						return typeof(ulong);
				}
				// Should never reach this
				Debug.Fail($"Unknown enum underlying type code of {UnderlyingTypeCode.AsString()}");
				return null;
			}
		}

		public static bool HasAnyDuplicateValues => _duplicateValues != null;
		#endregion

		// This static constructor caches the relevant enum information
		static EnumsCache()
		{
			var type = typeof(TEnum);
			Debug.Assert(type.IsEnum && type.IsValueType);
			var underlyingType = Enum.GetUnderlyingType(type);
			UnderlyingTypeCode = Type.GetTypeCode(underlyingType);
			IsFlagEnum = type.IsDefined(typeof(FlagsAttribute), false);

			var xParam = Expression.Parameter(type, "x");
			var yParam = Expression.Parameter(type, "y");
			var xParamConvert = Expression.Convert(xParam, underlyingType);
			var yParamConvert = Expression.Convert(yParam, underlyingType);
			Equal = Expression.Lambda<Func<TEnum, TEnum, bool>>(Expression.Equal(xParamConvert, yParamConvert), xParam, yParam).Compile();
			_greaterThan = Expression.Lambda<Func<TEnum, TEnum, bool>>(Expression.GreaterThan(xParamConvert, yParamConvert), xParam, yParam).Compile();
			GetHashCode = Expression.Lambda<Func<TEnum, int>>(Expression.Call(xParamConvert, underlyingType.GetMethod("GetHashCode")), xParam).Compile();
			_and = Expression.Lambda<Func<TEnum, TEnum, TEnum>>(Expression.Convert(Expression.And(xParamConvert, yParamConvert), type), xParam, yParam).Compile();
			_or = Expression.Lambda<Func<TEnum, TEnum, TEnum>>(Expression.Convert(Expression.Or(xParamConvert, yParamConvert), type), xParam, yParam).Compile();
			_xor = Expression.Lambda<Func<TEnum, TEnum, TEnum>>(Expression.Convert(Expression.ExclusiveOr(xParamConvert, yParamConvert), type), xParam, yParam).Compile();

			// Need to convert to long because sbyte and byte do not have subtract methods
			var xParamConvertToLong = Expression.Convert(xParamConvert, typeof(long));
			var isPowerOfTwo = Expression.Lambda<Func<TEnum, bool>>(Expression.Equal(Expression.And(xParamConvertToLong, Expression.Subtract(xParamConvertToLong, Expression.Constant(1L))), Expression.Constant(0L)), xParam).Compile();

			var fields = type.GetFields(BindingFlags.Public | BindingFlags.Static);
			_valueMap = new OrderedBiDirectionalDictionary<TEnum, NameAndAttributes>(fields.Length, new InternalEnumComparer<TEnum>(), null);
			if (fields.Length == 0)
			{
				return;
			}
			var duplicateValues = new Dictionary<string, ValueAndAttributes<TEnum>>();
			foreach (var field in fields)
			{
				var value = (TEnum)field.GetValue(null);
				var name = field.Name;
				var attributes = Attribute.GetCustomAttributes(field, false);
				var isMainDupe = false;
				if (attributes.Length > 0)
				{
					var descriptionFound = false;
					var enumMemberFound = false;
					for (var i = 0; i < attributes.Length; ++i)
					{
						if (!descriptionFound)
						{
							var descAttr = attributes[i] as DescriptionAttribute;
							if (descAttr != null)
							{
								for (var j = i; j > 0; --j)
								{
									attributes[j] = attributes[j - 1];
								}
								attributes[0] = descAttr;
								descriptionFound = true;
								if (enumMemberFound && isMainDupe)
								{
									break;
								}
							}
						}
						if (!enumMemberFound)
						{
							var enumMemberAttr = attributes[i] as EnumMemberAttribute;
							if (enumMemberAttr != null)
							{
								var endIndex = descriptionFound ? 1 : 0;
								for (var j = i; j > endIndex; --j)
								{
									attributes[j] = attributes[j - 1];
								}
								attributes[endIndex] = enumMemberAttr;
								enumMemberFound = true;
								if (descriptionFound && isMainDupe)
								{
									break;
								}
							}
						}
						if (!isMainDupe && (attributes[i] as MainDuplicateAttribute) != null)
						{
							isMainDupe = true;
							if (descriptionFound && enumMemberFound)
							{
								break;
							}
						}
					}
				}
				var index = _valueMap.IndexOfFirst(value);
				if (index < 0)
				{
					for (index = _valueMap.Count; index > 0; --index)
					{
						var mapValue = _valueMap.GetFirstAt(index - 1);
						if (!_greaterThan(mapValue, value))
						{
							break;
						}
					}
					_valueMap.Insert(index, value, new NameAndAttributes(name, attributes));
					if (isPowerOfTwo(value))
					{
						AllFlags = _or(AllFlags, value);
					}
				}
				else if (isMainDupe)
				{
					var nameAndAttributes = _valueMap.GetSecondAt(index);
					_valueMap.ReplaceSecondAt(index, new NameAndAttributes(name, attributes));
					duplicateValues.Add(nameAndAttributes.Name, new ValueAndAttributes<TEnum>(value, nameAndAttributes.Attributes));
				}
				else
				{
					duplicateValues.Add(name, new ValueAndAttributes<TEnum>(value, attributes));
				}
			}
			_maxDefined = _valueMap.GetFirstAt(_valueMap.Count - 1);
			_minDefined = _valueMap.GetFirstAt(0);
			if (UnderlyingTypeCode == TypeCode.UInt64)
			{
				IsContiguous = ToUInt64(_maxDefined) - ToUInt64(_minDefined) + 1UL == (ulong)_valueMap.Count;
			}
			else
			{
				IsContiguous = ToInt64(_maxDefined) - ToInt64(_minDefined) + 1L == _valueMap.Count;
			}

			if (duplicateValues.Count > 0)
			{
				// Makes sure is in increasing order, due to no removals
				var dupes = duplicateValues.OrderBy(pair => pair.Value.Value).ToList();
				_duplicateValues = new Dictionary<string, ValueAndAttributes<TEnum>>(duplicateValues.Count);
				foreach (var pair in dupes)
				{
					_duplicateValues.Add(pair.Key, pair.Value);
				}
			}
		}

		#region Standard Enum Operations
		#region Type Methods
		public static int GetDefinedCount(bool uniqueValued) => _valueMap.Count + (uniqueValued ? 0 : _duplicateValues?.Count ?? 0);

		public static EnumMemberInfo<TEnum>[] GetEnumMemberInfos(bool uniqueValued) => GetEnumMembersInValueOrder(uniqueValued).Select(info => info.ToEnumMemberInfo()).ToArray();

		public static string[] GetNames(bool uniqueValued) => (uniqueValued ? _valueMap.SecondItems.Select(nameAndAttr => nameAndAttr.Name) : GetEnumMembersInValueOrder(uniqueValued).Select(info => info.Name)).ToArray();

		public static TEnum[] GetValues(bool uniqueValued) => (uniqueValued ? _valueMap.FirstItems : GetEnumMembersInValueOrder(uniqueValued).Select(info => info.Value)).ToArray();

		public static string[] GetDescriptions(bool uniqueValued) => GetEnumMembersInValueOrder(uniqueValued).Select(info => info.Description).ToArray();

		public static string[] GetDescriptionsOrNames(bool uniqueValued) => GetEnumMembersInValueOrder(uniqueValued).Select(info => info.GetDescriptionOrName()).ToArray();

		public static string[] GetDescriptionsOrNames(Func<string, string> nameFormatter, bool uniqueValued)
		{
			return GetEnumMembersInValueOrder(uniqueValued).Select(info => info.GetDescriptionOrName(nameFormatter)).ToArray();
		}

		public static string[] GetEnumMemberValues(bool uniqueValued) => GetEnumMembersInValueOrder(uniqueValued).Select(info => info.EnumMemberValue).ToArray();

		public static Attribute[][] GetAllAttributes(bool uniqueValued) => GetEnumMembersInValueOrder(uniqueValued).Select(info => info.Attributes).ToArray();

		public static TAttribute[] GetAttributes<TAttribute>(bool uniqueValued)
			where TAttribute : Attribute
		{
			return GetEnumMembersInValueOrder(uniqueValued).Select(info => info.GetAttribute<TAttribute>()).ToArray();
		}

		public static int Compare(TEnum x, TEnum y)
		{
			if (_greaterThan(x, y))
			{
				return 1;
			}
			if (_greaterThan(y, x))
			{
				return -1;
			}
			return 0;
		}

		private static IEnumerable<InternalEnumMemberInfo<TEnum>> GetEnumMembersInValueOrder(bool uniqueValued)
		{
			if (uniqueValued)
			{
				return _valueMap.Select(pair => new InternalEnumMemberInfo<TEnum>(pair));
			}
			else
			{
				return GetAllEnumMembersInValueOrder();
			}
		}

		private static IEnumerable<InternalEnumMemberInfo<TEnum>> GetAllEnumMembersInValueOrder()
		{
			using (var mainEnumerator = _valueMap.GetEnumerator())
			{
				var mainIsActive = mainEnumerator.MoveNext();
				var mainPair = mainIsActive ? mainEnumerator.Current : new Pair<TEnum, NameAndAttributes>();
				using (IEnumerator<KeyValuePair<string, ValueAndAttributes<TEnum>>> dupeEnumerator = _duplicateValues?.GetEnumerator())
				{
					var dupeIsActive = dupeEnumerator?.MoveNext() ?? false;
					var dupePair = dupeIsActive ? dupeEnumerator.Current : new KeyValuePair<string, ValueAndAttributes<TEnum>>();
					var count = GetDefinedCount(false);
					for (var i = 0; i < count; ++i)
					{
						InternalEnumMemberInfo<TEnum> info;
						if (dupeIsActive && (!mainIsActive || _greaterThan(mainPair.First, dupePair.Value.Value)))
						{
							info = new InternalEnumMemberInfo<TEnum>(dupePair);
							if (dupeEnumerator.MoveNext())
							{
								dupePair = dupeEnumerator.Current;
							}
							else
							{
								dupeIsActive = false;
							}
						}
						else
						{
							info = new InternalEnumMemberInfo<TEnum>(mainPair);
							if (mainEnumerator.MoveNext())
							{
								mainPair = mainEnumerator.Current;
							}
							else
							{
								mainIsActive = false;
							}
						}
						yield return info;
					}
				}
			}
		}
		#endregion

		#region IsValid
		public static bool IsValid(object value)
		{
			TEnum result;
			return TryToEnum(value, out result);
		}

		public static bool IsValid(TEnum value) => IsFlagEnum ? IsValidFlagCombination(value) : IsDefined(value);

		public static bool IsValid(long value) => IsWithinUnderlyingTypesValueRange(value) && IsValid(InternalToEnum(value));

		public static bool IsValid(ulong value) => IsWithinUnderlyingTypesValueRange(value) && IsValid(InternalToEnum(value));
		#endregion

		#region IsDefined
		public static bool IsDefined(object value)
		{
			TEnum result;
			return TryToEnum(value, out result, false) && IsDefined(result);
		}

		public static bool IsDefined(TEnum value) => IsContiguous ? !(_greaterThan(_minDefined, value) || _greaterThan(value, _maxDefined)) : _valueMap.ContainsFirst(value);

		public static bool IsDefined(string name, bool ignoreCase = false)
		{
			Preconditions.NotNull(name, nameof(name));

			return _valueMap.ContainsSecond(new NameAndAttributes(name)) || (_duplicateValues?.ContainsKey(name) ?? false) || (ignoreCase && _ignoreCaseSet.Value.ContainsKey(name));
		}

		public static bool IsDefined(long value) => IsWithinUnderlyingTypesValueRange(value) && IsDefined(InternalToEnum(value));

		public static bool IsDefined(ulong value) => IsWithinUnderlyingTypesValueRange(value) && IsDefined(InternalToEnum(value));
		#endregion

		#region IsWithinUnderlyingTypesValueRange
		public static bool IsWithinUnderlyingTypesValueRange(long value)
		{
			switch (UnderlyingTypeCode)
			{
				case TypeCode.Int32:
					return value >= int.MinValue && value <= int.MaxValue;
				case TypeCode.UInt32:
					return value >= uint.MinValue && value <= uint.MaxValue;
				case TypeCode.SByte:
					return value >= sbyte.MinValue && value <= sbyte.MaxValue;
				case TypeCode.Byte:
					return value >= byte.MinValue && value <= byte.MaxValue;
				case TypeCode.Int16:
					return value >= short.MinValue && value <= short.MaxValue;
				case TypeCode.UInt16:
					return value >= ushort.MinValue && value <= ushort.MaxValue;
				case TypeCode.Int64:
					return true;
				case TypeCode.UInt64:
					return value >= 0L;
			}
			// Should never reach this
			Debug.Fail($"Unknown enum underlying type code of {EnumsCache<TypeCode>.AsString(UnderlyingTypeCode)}");
			return false;
		}

		public static bool IsWithinUnderlyingTypesValueRange(ulong value)
		{
			return UnderlyingTypeCode == TypeCode.UInt64 || (value <= long.MaxValue && IsWithinUnderlyingTypesValueRange((long)value));
		}
		#endregion

		#region ToEnum
		public static TEnum ToEnum(object value, bool validate = true)
		{
			Preconditions.NotNull(value, nameof(value));

			if (value is TEnum)
			{
				var result = (TEnum)value;
				if (validate)
				{
					Validate(result, nameof(value));
				}
				return result;
			}

			switch (Type.GetTypeCode(value.GetType()))
			{
				case TypeCode.SByte:
					return ToEnum((sbyte)value, validate);
				case TypeCode.Byte:
					return ToEnum((byte)value, validate);
				case TypeCode.Int16:
					return ToEnum((short)value, validate);
				case TypeCode.UInt16:
					return ToEnum((ushort)value, validate);
				case TypeCode.Int32:
					return ToEnum((int)value, validate);
				case TypeCode.UInt32:
					return ToEnum((uint)value, validate);
				case TypeCode.Int64:
					return ToEnum((long)value, validate);
				case TypeCode.UInt64:
					return ToEnum((ulong)value, validate);
				case TypeCode.String:
					var result = Parse((string)value);
					if (validate)
					{
						Validate(result, nameof(value));
					}
					return result;
			}
			throw new ArgumentException($"value is not type {typeof(TEnum).Name}, SByte, Int16, Int32, Int64, Byte, UInt16, UInt32, UInt64, or String.");
		}

		public static TEnum ToEnum(long value, bool validate = true)
		{
			ValidateForOverflow(value);

			var result = InternalToEnum(value);
			if (validate)
			{
				Validate(result, nameof(value));
			}
			return result;
		}

		public static TEnum ToEnum(ulong value, bool validate = true)
		{
			ValidateForOverflow(value);

			var result = InternalToEnum(value);
			if (validate)
			{
				Validate(result, nameof(value));
			}
			return result;
		}

		public static TEnum ToEnumOrDefault(object value, TEnum defaultEnum, bool validate = true)
		{
			TEnum result;
			if (!TryToEnum(value, out result, validate))
			{
				result = defaultEnum;
			}
			return result;
		}

		public static TEnum ToEnumOrDefault(long value, TEnum defaultEnum, bool validate = true)
		{
			TEnum result;
			if (!TryToEnum(value, out result, validate))
			{
				result = defaultEnum;
			}
			return result;
		}

		public static TEnum ToEnumOrDefault(ulong value, TEnum defaultEnum, bool validate = true)
		{
			TEnum result;
			if (!TryToEnum(value, out result, validate))
			{
				result = defaultEnum;
			}
			return result;
		}

		public static bool TryToEnum(object value, out TEnum result, bool validate = true)
		{
			Preconditions.NotNull(value, nameof(value));

			if (value is TEnum)
			{
				result = (TEnum)value;
				return true;
			}

			switch (Type.GetTypeCode(value.GetType()))
			{
				case TypeCode.SByte:
					return TryToEnum((sbyte)value, out result, validate);
				case TypeCode.Byte:
					return TryToEnum((byte)value, out result, validate);
				case TypeCode.Int16:
					return TryToEnum((short)value, out result, validate);
				case TypeCode.UInt16:
					return TryToEnum((ushort)value, out result, validate);
				case TypeCode.Int32:
					return TryToEnum((int)value, out result, validate);
				case TypeCode.UInt32:
					return TryToEnum((uint)value, out result, validate);
				case TypeCode.Int64:
					return TryToEnum((long)value, out result, validate);
				case TypeCode.UInt64:
					return TryToEnum((ulong)value, out result, validate);
				case TypeCode.String:
					if (TryParse((string)value, out result))
					{
						if (!validate || IsValid(result))
						{
							return true;
						}
					}
					break;
			}
			result = default(TEnum);
			return false;
		}

		public static bool TryToEnum(long value, out TEnum result, bool validate = true)
		{
			if (IsWithinUnderlyingTypesValueRange(value))
			{
				result = InternalToEnum(value);
				if (!validate || IsValid(result))
				{
					return true;
				}
			}
			result = default(TEnum);
			return false;
		}

		public static bool TryToEnum(ulong value, out TEnum result, bool validate = true)
		{
			if (IsWithinUnderlyingTypesValueRange(value))
			{
				result = InternalToEnum(value);
				if (!validate || IsValid(result))
				{
					return true;
				}
			}
			result = default(TEnum);
			return false;
		}

		private static TEnum InternalToEnum(long value) => (TEnum)Enum.ToObject(typeof(TEnum), value);

		private static TEnum InternalToEnum(ulong value) => (TEnum)Enum.ToObject(typeof(TEnum), value);
		#endregion

		#region All Values Main Methods
		public static TEnum Validate(TEnum value, string paramName)
		{
			if (!IsValid(value))
			{
				throw new ArgumentException($"invalid value of {AsString(value)} for {typeof(TEnum).Name}", paramName);
			}
			return value;
		}

		public static string AsString(TEnum value)
		{
			if (IsFlagEnum && IsValidFlagCombination(value))
			{
				return FormatAsFlags(value);
			}
			return Format(value, EnumFormat.Name, EnumFormat.DecimalValue);
		}

		public static string AsString(TEnum value, params EnumFormat[] formats) => formats?.Length > 0 ? InternalFormat(value, formats) : AsString(value);

		public static string AsString(TEnum value, string format) => string.IsNullOrEmpty(format) ? AsString(value) : Format(value, format);

		public static string AsString(string name)
		{
			var info = GetInternalEnumMemberInfo(name);
			if (info.Name == null)
			{
				return null;
			}
			if (IsFlagEnum && IsValidFlagCombination(info.Value))
			{
				return FormatAsFlags(info.Value);
			}
			return InternalFormat(info.Value, info, EnumFormat.Name, EnumFormat.DecimalValue);
		}

		public static string AsString(string name, params EnumFormat[] formats)
		{
			var info = GetInternalEnumMemberInfo(name);
			if (info.Name == null)
			{
				return null;
			}
			return InternalFormat(info.Value, info, formats?.Length > 0 ? formats : new[] { EnumFormat.Name, EnumFormat.DecimalValue });
		}

		public static string AsString(string name, string format) => string.IsNullOrEmpty(format) ? AsString(name) : Format(name, format);

		public static string Format(TEnum value, params EnumFormat[] formats)
		{
			Preconditions.NotNullOrEmpty(formats, nameof(formats));

			return InternalFormat(value, formats);
		}

		public static string Format(TEnum value, string format)
		{
			Preconditions.NotNull(format, nameof(format));

			switch (format)
			{
				case "G":
				case "g":
					return AsString(value);
				case "F":
				case "f":
					return FormatAsFlags(value) ?? AsString(value);
				case "D":
				case "d":
					return ToDecimalString(value);
				case "X":
				case "x":
					return ToHexadecimalString(value);
			}
			throw new FormatException("format string can be only \"G\", \"g\", \"X\", \"x\", \"F\", \"f\", \"D\" or \"d\".");
		}

		public static string Format(string name, params EnumFormat[] formats)
		{
			Preconditions.NotNullOrEmpty(formats, nameof(formats));

			var info = GetInternalEnumMemberInfo(name);
			if (info.Name == null)
			{
				return null;
			}
			return InternalFormat(info.Value, info, formats);
		}

		public static string Format(string name, string format)
		{
			Preconditions.NotNull(format, nameof(format));

			var info = GetInternalEnumMemberInfo(name);
			if (info.Name == null)
			{
				return null;
			}

			switch (format)
			{
				case "G":
				case "g":
				case "F":
				case "f":
					return InternalFormat(info.Value, info, EnumFormat.Name, EnumFormat.DecimalValue);
				case "D":
				case "d":
					return ToDecimalString(info.Value);
				case "X":
				case "x":
					return ToHexadecimalString(info.Value);
			}
			throw new FormatException("format string can be only \"G\", \"g\", \"X\", \"x\", \"F\", \"f\", \"D\" or \"d\".");
		}

		private static string InternalFormat(TEnum value, params EnumFormat[] formats)
		{
			return InternalFormat(value, GetInternalEnumMemberInfo(value), formats);
		}

		private static string InternalFormat(TEnum value, InternalEnumMemberInfo<TEnum> info, params EnumFormat[] formats)
		{
			foreach (var format in formats)
			{
				switch (format)
				{
					case EnumFormat.DecimalValue:
						return ToDecimalString(value);
					case EnumFormat.HexadecimalValue:
						return ToHexadecimalString(value);
					case EnumFormat.Name:
						if (info.Name != null)
						{
							return info.Name;
						}
						break;
					case EnumFormat.Description:
						var description = info.Description;
						if (description != null)
						{
							return description;
						}
						break;
					case EnumFormat.EnumMemberValue:
						var enumMemberValue = info.EnumMemberValue;
						if (enumMemberValue != null)
						{
							return enumMemberValue;
						}
						break;
				}
			}
			return null;
		}

		private static string ToDecimalString(TEnum value)
		{
			object o = value;
			switch (UnderlyingTypeCode)
			{
				case TypeCode.Int32:
					return ((int)o).ToString("D");
				case TypeCode.UInt32:
					return ((uint)o).ToString("D");
				case TypeCode.SByte:
					return ((sbyte)o).ToString("D");
				case TypeCode.Byte:
					return ((byte)o).ToString("D");
				case TypeCode.Int16:
					return ((short)o).ToString("D");
				case TypeCode.UInt16:
					return ((ushort)o).ToString("D");
				case TypeCode.Int64:
					return ((long)o).ToString("D");
				case TypeCode.UInt64:
					return ((ulong)o).ToString("D");
			}
			// Should never reach this
			Debug.Fail($"Unknown enum underlying type code of {UnderlyingTypeCode.AsString()}");
			return null;
		}

		private static string ToHexadecimalString(TEnum value)
		{
			object o = value;
			switch (UnderlyingTypeCode)
			{
				case TypeCode.Int32:
					return ((int)o).ToString("X8");
				case TypeCode.UInt32:
					return ((uint)o).ToString("X8");
				case TypeCode.SByte:
					return ((sbyte)o).ToString("X2");
				case TypeCode.Byte:
					return ((byte)o).ToString("X2");
				case TypeCode.Int16:
					return ((short)o).ToString("X4");
				case TypeCode.UInt16:
					return ((ushort)o).ToString("X4");
				case TypeCode.Int64:
					return ((long)o).ToString("X16");
				case TypeCode.UInt64:
					return ((ulong)o).ToString("X16");
			}
			// Should never reach this
			Debug.Fail($"Unknown enum underlying type code of {UnderlyingTypeCode.AsString()}");
			return null;
		}

		public static object GetUnderlyingValue(TEnum value)
		{
			object o = value;
			switch (UnderlyingTypeCode)
			{
				case TypeCode.Int32:
					return (int)o;
				case TypeCode.UInt32:
					return (uint)o;
				case TypeCode.SByte:
					return (sbyte)o;
				case TypeCode.Byte:
					return (byte)o;
				case TypeCode.Int16:
					return (short)o;
				case TypeCode.UInt16:
					return (ushort)o;
				case TypeCode.Int64:
					return (long)o;
				case TypeCode.UInt64:
					return (ulong)o;
			}
			// Should never reach this
			Debug.Fail($"Unknown enum underlying type code of {UnderlyingTypeCode.AsString()}");
			return null;
		}

		public static sbyte ToSByte(TEnum value)
		{
			object o = value;
			switch (UnderlyingTypeCode)
			{
				case TypeCode.Int32:
					return Convert.ToSByte((int)o);
				case TypeCode.UInt32:
					return Convert.ToSByte((uint)o);
				case TypeCode.SByte:
					return (sbyte)o;
				case TypeCode.Byte:
					return Convert.ToSByte((byte)o);
				case TypeCode.Int16:
					return Convert.ToSByte((short)o);
				case TypeCode.UInt16:
					return Convert.ToSByte((ushort)o);
				case TypeCode.Int64:
					return Convert.ToSByte((long)o);
				case TypeCode.UInt64:
					return Convert.ToSByte((ulong)o);
			}
			// Should never reach this
			Debug.Fail($"Unknown enum underlying type code of {UnderlyingTypeCode.AsString()}");
			return 0;
		}

		public static byte ToByte(TEnum value)
		{
			object o = value;
			switch (UnderlyingTypeCode)
			{
				case TypeCode.Int32:
					return Convert.ToByte((int)o);
				case TypeCode.UInt32:
					return Convert.ToByte((uint)o);
				case TypeCode.SByte:
					return Convert.ToByte((sbyte)o);
				case TypeCode.Byte:
					return (byte)o;
				case TypeCode.Int16:
					return Convert.ToByte((short)o);
				case TypeCode.UInt16:
					return Convert.ToByte((ushort)o);
				case TypeCode.Int64:
					return Convert.ToByte((long)o);
				case TypeCode.UInt64:
					return Convert.ToByte((ulong)o);
			}
			// Should never reach this
			Debug.Fail($"Unknown enum underlying type code of {UnderlyingTypeCode.AsString()}");
			return 0;
		}

		public static short ToInt16(TEnum value)
		{
			object o = value;
			switch (UnderlyingTypeCode)
			{
				case TypeCode.Int32:
					return Convert.ToInt16((int)o);
				case TypeCode.UInt32:
					return Convert.ToInt16((uint)o);
				case TypeCode.SByte:
					return (sbyte)o;
				case TypeCode.Byte:
					return (byte)o;
				case TypeCode.Int16:
					return (short)o;
				case TypeCode.UInt16:
					return Convert.ToInt16((ushort)o);
				case TypeCode.Int64:
					return Convert.ToInt16((long)o);
				case TypeCode.UInt64:
					return Convert.ToInt16((ulong)o);
			}
			// Should never reach this
			Debug.Fail($"Unknown enum underlying type code of {UnderlyingTypeCode.AsString()}");
			return 0;
		}

		public static ushort ToUInt16(TEnum value)
		{
			object o = value;
			switch (UnderlyingTypeCode)
			{
				case TypeCode.Int32:
					return Convert.ToUInt16((int)o);
				case TypeCode.UInt32:
					return Convert.ToUInt16((uint)o);
				case TypeCode.SByte:
					return Convert.ToUInt16((sbyte)o);
				case TypeCode.Byte:
					return (byte)o;
				case TypeCode.Int16:
					return Convert.ToUInt16((short)o);
				case TypeCode.UInt16:
					return (ushort)o;
				case TypeCode.Int64:
					return Convert.ToUInt16((long)o);
				case TypeCode.UInt64:
					return Convert.ToUInt16((ulong)o);
			}
			// Should never reach this
			Debug.Fail($"Unknown enum underlying type code of {UnderlyingTypeCode.AsString()}");
			return 0;
		}

		public static int ToInt32(TEnum value)
		{
			object o = value;
			switch (UnderlyingTypeCode)
			{
				case TypeCode.Int32:
					return (int)o;
				case TypeCode.UInt32:
					return Convert.ToInt32((uint)o);
				case TypeCode.SByte:
					return (sbyte)o;
				case TypeCode.Byte:
					return (byte)o;
				case TypeCode.Int16:
					return (short)o;
				case TypeCode.UInt16:
					return (ushort)o;
				case TypeCode.Int64:
					return Convert.ToInt32((long)o);
				case TypeCode.UInt64:
					return Convert.ToInt32((ulong)o);
			}
			// Should never reach this
			Debug.Fail($"Unknown enum underlying type code of {UnderlyingTypeCode.AsString()}");
			return 0;
		}

		public static uint ToUInt32(TEnum value)
		{
			object o = value;
			switch (UnderlyingTypeCode)
			{
				case TypeCode.Int32:
					return Convert.ToUInt32((int)o);
				case TypeCode.UInt32:
					return (uint)o;
				case TypeCode.SByte:
					return Convert.ToUInt32((sbyte)o);
				case TypeCode.Byte:
					return (byte)o;
				case TypeCode.Int16:
					return Convert.ToUInt32((short)o);
				case TypeCode.UInt16:
					return (ushort)o;
				case TypeCode.Int64:
					return Convert.ToUInt32((long)o);
				case TypeCode.UInt64:
					return Convert.ToUInt32((ulong)o);
			}
			// Should never reach this
			Debug.Fail($"Unknown enum underlying type code of {UnderlyingTypeCode.AsString()}");
			return 0;
		}

		public static long ToInt64(TEnum value)
		{
			object o = value;
			switch (UnderlyingTypeCode)
			{
				case TypeCode.Int32:
					return (int)o;
				case TypeCode.UInt32:
					return (uint)o;
				case TypeCode.SByte:
					return (sbyte)o;
				case TypeCode.Byte:
					return (byte)o;
				case TypeCode.Int16:
					return (short)o;
				case TypeCode.UInt16:
					return (ushort)o;
				case TypeCode.Int64:
					return (long)o;
				case TypeCode.UInt64:
					return Convert.ToInt64((ulong)o);
			}
			// Should never reach this
			Debug.Fail($"Unknown enum underlying type code of {UnderlyingTypeCode.AsString()}");
			return 0;
		}

		public static ulong ToUInt64(TEnum value)
		{
			object o = value;
			switch (UnderlyingTypeCode)
			{
				case TypeCode.Int32:
					return Convert.ToUInt64((int)o);
				case TypeCode.UInt32:
					return (uint)o;
				case TypeCode.SByte:
					return Convert.ToUInt64((sbyte)o);
				case TypeCode.Byte:
					return (byte)o;
				case TypeCode.Int16:
					return Convert.ToUInt64((short)o);
				case TypeCode.UInt16:
					return (ushort)o;
				case TypeCode.Int64:
					return Convert.ToUInt64((long)o);
				case TypeCode.UInt64:
					return (ulong)o;
			}
			// Should never reach this
			Debug.Fail($"Unknown enum underlying type code of {UnderlyingTypeCode.AsString()}");
			return 0;
		}
		#endregion

		#region Defined Values Main Methods
		public static EnumMemberInfo<TEnum> GetEnumMemberInfo(TEnum value)
		{
			return GetInternalEnumMemberInfo(value).ToEnumMemberInfo();
		}

		public static EnumMemberInfo<TEnum> GetEnumMemberInfo(string name)
		{
			return GetInternalEnumMemberInfo(name).ToEnumMemberInfo();
		}

		public static string GetName(TEnum value)
		{
			return GetInternalEnumMemberInfo(value).Name;
		}

		public static string GetDescription(TEnum value)
		{
			return GetInternalEnumMemberInfo(value).Description;
		}

		public static string GetDescription(string name)
		{
			return GetInternalEnumMemberInfo(name).Description;
		}

		public static string GetDescriptionOrName(TEnum value)
		{
			return GetInternalEnumMemberInfo(value).GetDescriptionOrName();
		}

		public static string GetDescriptionOrName(TEnum value, Func<string, string> nameFormatter)
		{
			return GetInternalEnumMemberInfo(value).GetDescriptionOrName(nameFormatter);
		}

		public static string GetDescriptionOrName(string name)
		{
			return GetInternalEnumMemberInfo(name).GetDescriptionOrName();
		}

		public static string GetDescriptionOrName(string name, Func<string, string> nameFormatter)
		{
			return GetInternalEnumMemberInfo(name).GetDescriptionOrName(nameFormatter);
		}

		public static string GetEnumMemberValue(TEnum value)
		{
			return GetInternalEnumMemberInfo(value).EnumMemberValue;
		}

		public static string GetEnumMemberValue(string name)
		{
			return GetInternalEnumMemberInfo(name).EnumMemberValue;
		}

		private static InternalEnumMemberInfo<TEnum> GetInternalEnumMemberInfo(TEnum value)
		{
			var index = _valueMap.IndexOfFirst(value);
			return index >= 0 ? new InternalEnumMemberInfo<TEnum>(_valueMap.GetAt(index)) : new InternalEnumMemberInfo<TEnum>();
		}

		private static InternalEnumMemberInfo<TEnum> GetInternalEnumMemberInfo(string name)
		{
			Preconditions.NotNull(name, nameof(name));

			var index = _valueMap.IndexOfSecond(new NameAndAttributes(name));
			if (index >= 0)
			{
				return new InternalEnumMemberInfo<TEnum>(_valueMap.GetAt(index));
			}
			ValueAndAttributes<TEnum> valueAndAttributes;
			if (_duplicateValues != null && _duplicateValues.TryGetValue(name, out valueAndAttributes))
			{
				return new InternalEnumMemberInfo<TEnum>(name, valueAndAttributes);
			}
			return new InternalEnumMemberInfo<TEnum>();
		}
		#endregion

		#region Attributes
		public static bool HasAttribute<TAttribute>(TEnum value)
			where TAttribute : Attribute
		{
			return GetAttribute<TAttribute>(value) != null;
		}

		public static bool HasAttribute<TAttribute>(string name)
			where TAttribute : Attribute
		{
			return GetAttribute<TAttribute>(name) != null;
		}

		public static TAttribute GetAttribute<TAttribute>(TEnum value)
			where TAttribute : Attribute
		{
			return GetInternalEnumMemberInfo(value).GetAttribute<TAttribute>();
		}

		public static TAttribute GetAttribute<TAttribute>(string name)
			where TAttribute : Attribute
		{
			return GetInternalEnumMemberInfo(name).GetAttribute<TAttribute>();
		}

		public static TResult GetAttributeSelect<TAttribute, TResult>(TEnum value, Func<TAttribute, TResult> selector, TResult defaultValue)
			where TAttribute : Attribute
		{
			TResult result;
			if (!TryGetAttributeSelect(value, selector, out result))
			{
				result = defaultValue;
			}
			return result;
		}

		public static TResult GetAttributeSelect<TAttribute, TResult>(string name, Func<TAttribute, TResult> selector, TResult defaultValue)
			where TAttribute : Attribute
		{
			TResult result;
			if (!TryGetAttributeSelect(name, selector, out result))
			{
				result = defaultValue;
			}
			return result;
		}

		public static bool TryGetAttributeSelect<TAttribute, TResult>(TEnum value, Func<TAttribute, TResult> selector, out TResult result)
			where TAttribute : Attribute
		{
			Preconditions.NotNull(selector, nameof(selector));

			var attr = GetAttribute<TAttribute>(value);
			if (attr != null)
			{
				result = selector(attr);
				return true;
			}
			result = default(TResult);
			return false;
		}

		public static bool TryGetAttributeSelect<TAttribute, TResult>(string name, Func<TAttribute, TResult> selector, out TResult result)
			where TAttribute : Attribute
		{
			Preconditions.NotNull(selector, nameof(selector));

			var attr = GetAttribute<TAttribute>(name);
			if (attr != null)
			{
				result = selector(attr);
				return true;
			}
			result = default(TResult);
			return false;
		}

		public static TAttribute[] GetAttributes<TAttribute>(TEnum value)
			where TAttribute : Attribute
		{
			return GetInternalEnumMemberInfo(value).GetAttributes<TAttribute>();
		}

		public static TAttribute[] GetAttributes<TAttribute>(string name)
			where TAttribute : Attribute
		{
			return GetInternalEnumMemberInfo(name).GetAttributes<TAttribute>();
		}

		public static Attribute[] GetAllAttributes(TEnum value)
		{
			return GetInternalEnumMemberInfo(value).Attributes;
		}

		public static Attribute[] GetAllAttributes(string name)
		{
			return GetInternalEnumMemberInfo(name).Attributes;
		}
		#endregion

		#region Parsing
		public static TEnum Parse(string value, bool ignoreCase = false) => Parse(value, ignoreCase, null);

		public static TEnum Parse(string value, params EnumFormat[] parseFormatOrder) => Parse(value, false, parseFormatOrder);

		public static TEnum Parse(string value, bool ignoreCase, params EnumFormat[] parseFormatOrder)
		{
			Preconditions.NotNull(value, nameof(value));

			value = value.Trim();
			TEnum result;
			if (IsFlagEnum)
			{
				return TryParseNumeric(value, NumberStyles.AllowLeadingSign, out result) ? result : ParseFlags(value, ignoreCase, FlagEnums.DefaultDelimiter, parseFormatOrder);
			}

			if (!(parseFormatOrder?.Length > 0))
			{
				parseFormatOrder = new[] { EnumFormat.Name, EnumFormat.DecimalValue };
			}

			if (InternalTryParse(value, ignoreCase, out result, parseFormatOrder))
			{
				return result;
			}
			if (Enums.IsNumeric(value))
			{
				throw Enums.GetOverflowException();
			}
			throw new ArgumentException($"string was not recognized as being a member of {typeof(TEnum).Name}", nameof(value));
		}

		public static TEnum ParseOrDefault(string value, TEnum defaultEnum) => ParseOrDefault(value, false, defaultEnum, null);

		public static TEnum ParseOrDefault(string value, TEnum defaultEnum, params EnumFormat[] parseFormatOrder) => ParseOrDefault(value, false, defaultEnum, parseFormatOrder);

		public static TEnum ParseOrDefault(string value, bool ignoreCase, TEnum defaultEnum) => ParseOrDefault(value, ignoreCase, defaultEnum, null);

		public static TEnum ParseOrDefault(string value, bool ignoreCase, TEnum defaultEnum, params EnumFormat[] parseFormatOrder)
		{
			TEnum result;
			if (!TryParse(value, ignoreCase, out result, parseFormatOrder))
			{
				result = defaultEnum;
			}
			return result;
		}

		public static bool TryParse(string value, out TEnum result) => TryParse(value, false, out result, null);

		public static bool TryParse(string value, bool ignoreCase, out TEnum result) => TryParse(value, ignoreCase, out result, null);

		public static bool TryParse(string value, out TEnum result, params EnumFormat[] parseFormatOrder) => TryParse(value, false, out result, parseFormatOrder);

		public static bool TryParse(string value, bool ignoreCase, out TEnum result, params EnumFormat[] parseFormatOrder)
		{
			if (value != null)
			{
				value = value.Trim();
				if (IsFlagEnum)
				{
					return TryParseNumeric(value, NumberStyles.AllowLeadingSign, out result) || TryParseFlags(value, ignoreCase, FlagEnums.DefaultDelimiter, out result, parseFormatOrder);
				}

				if (!(parseFormatOrder?.Length > 0))
				{
					parseFormatOrder = new[] { EnumFormat.Name, EnumFormat.DecimalValue };
				}

				return InternalTryParse(value, ignoreCase, out result, parseFormatOrder);
			}
			result = default(TEnum);
			return false;
		}

		private static bool InternalTryParse(string value, bool ignoreCase, out TEnum result, EnumFormat[] parseFormatOrder)
		{
			foreach (var parseFormat in parseFormatOrder)
			{
				switch (parseFormat)
				{
					case EnumFormat.DecimalValue:
					case EnumFormat.HexadecimalValue:
						if (TryParseNumeric(value, parseFormat == EnumFormat.DecimalValue ? NumberStyles.AllowLeadingSign : NumberStyles.AllowHexSpecifier, out result))
						{
							return true;
						}
						break;
					case EnumFormat.Name:
						if (TryParseName(value, ignoreCase, out result))
						{
							return true;
						}
						break;
					case EnumFormat.Description:
						if (TryParseDescription(value, ignoreCase, out result))
						{
							return true;
						}
						break;
					case EnumFormat.EnumMemberValue:
						if (TryParseEnumMemberValue(value, ignoreCase, out result))
						{
							return true;
						}
						break;
				}
			}
			result = default(TEnum);
			return false;
		}

		private static bool TryParseNumeric(string value, NumberStyles style, out TEnum result)
		{
			if (UnderlyingTypeCode == TypeCode.UInt64)
			{
				ulong resultAsULong;
				if (ulong.TryParse(value, style, null, out resultAsULong))
				{
					result = InternalToEnum(resultAsULong);
					return true;
				}
			}
			else
			{
				long resultAsLong;
				if (long.TryParse(value, style, null, out resultAsLong) && IsWithinUnderlyingTypesValueRange(resultAsLong))
				{
					result = InternalToEnum(resultAsLong);
					return true;
				}
			}
			result = default(TEnum);
			return false;
		}

		private static bool TryParseName(string value, bool ignoreCase, out TEnum result)
		{
			if (_valueMap.TryGetFirst(new NameAndAttributes(value), out result))
			{
				return true;
			}
			ValueAndAttributes<TEnum> valueAndAttributes;
			if (_duplicateValues != null && _duplicateValues.TryGetValue(value, out valueAndAttributes))
			{
				result = valueAndAttributes.Value;
				return true;
			}
			if (ignoreCase)
			{
				string name;
				if (_ignoreCaseSet.Value.TryGetValue(value, out name))
				{
					if (!_valueMap.TryGetFirst(new NameAndAttributes(name), out result))
					{
						result = _duplicateValues[name].Value;
					}
					return true;
				}
			}
			return false;
		}

		private static bool TryParseDescription(string description, bool ignoreCase, out TEnum result)
		{
			string name;
			if (_descriptionNameMap.Value.TryGetValue(description, out name) || (ignoreCase && _descriptionIgnoreCase.Value.TryGetValue(description, out name)))
			{
				if (!_valueMap.TryGetFirst(new NameAndAttributes(name), out result))
				{
					result = _duplicateValues[name].Value;
				}
				return true;
			}
			result = default(TEnum);
			return false;
		}

		private static bool TryParseEnumMemberValue(string enumMemberValue, bool ignoreCase, out TEnum result)
		{
			string name;
			if (_enumMemberValues.Value.TryGetValue(enumMemberValue, out name) || (ignoreCase && _enumMemberValuesIgnoreCase.Value.TryGetValue(enumMemberValue, out name)))
			{
				if (!_valueMap.TryGetFirst(new NameAndAttributes(name), out result))
				{
					result = _duplicateValues[name].Value;
				}
				return true;
			}
			result = default(TEnum);
			return false;
		}
		#endregion

		#region Overflow Methods
		private static void ValidateForOverflow(ulong value)
		{
			if (!IsWithinUnderlyingTypesValueRange(value))
			{
				throw Enums.GetOverflowException();
			}
		}

		private static void ValidateForOverflow(long value)
		{
			if (!IsWithinUnderlyingTypesValueRange(value))
			{
				throw Enums.GetOverflowException();
			}
		}
		#endregion
		#endregion

		#region Flag Enum Operations
		#region Main Methods
		public static bool IsValidFlagCombination(TEnum value) => Equal(_and(AllFlags, value), value);

		public static string FormatAsFlags(TEnum value, EnumFormat[] formats) => FormatAsFlags(value, FlagEnums.DefaultDelimiter, formats);

		public static string FormatAsFlags(TEnum value, string delimiter = FlagEnums.DefaultDelimiter, EnumFormat[] formats = null)
		{
			Preconditions.NotNullOrEmpty(delimiter, nameof(delimiter));

			if (!IsValidFlagCombination(value))
			{
				return null;
			}

			if (!(formats?.Length > 0))
			{
				formats = new[] { EnumFormat.Name, EnumFormat.DecimalValue };
			}

			TEnum[] flags;
			var info = GetInternalEnumMemberInfo(value);
			if (info.Name != null || (flags = InternalGetFlags(value)).Length == 0)
			{
				return InternalFormat(value, info, formats);
			}

			var values = new string[flags.Length];
			for (var i = 0; i < flags.Length; ++i)
			{
				values[i] = InternalFormat(flags[i], formats);
			}
			return string.Join(delimiter, values);
		}

		public static TEnum[] GetFlags(TEnum value)
		{
			return IsValidFlagCombination(value) ? InternalGetFlags(value) : null;
		}

		public static bool HasAnyFlags(TEnum value)
		{
			ValidateIsValidFlagCombination(value, nameof(value));
			return !Equal(value, default(TEnum));
		}

		public static bool HasAnyFlags(TEnum value, TEnum flagMask)
		{
			ValidateIsValidFlagCombination(value, nameof(value));
			ValidateIsValidFlagCombination(flagMask, nameof(flagMask));
			return InternalHasAnyFlags(value, flagMask);
		}

		public static bool HasAllFlags(TEnum value)
		{
			ValidateIsValidFlagCombination(value, nameof(value));
			return Equal(value, AllFlags);
		}

		public static bool HasAllFlags(TEnum value, TEnum flagMask)
		{
			ValidateIsValidFlagCombination(value, nameof(value));
			ValidateIsValidFlagCombination(flagMask, nameof(flagMask));
			return Equal(_and(value, flagMask), flagMask);
		}

		public static TEnum InvertFlags(TEnum value)
		{
			ValidateIsValidFlagCombination(value, nameof(value));
			return _xor(value, AllFlags);
		}

		public static TEnum InvertFlags(TEnum value, TEnum flagMask)
		{
			ValidateIsValidFlagCombination(value, nameof(value));
			ValidateIsValidFlagCombination(flagMask, nameof(flagMask));
			return _xor(value, flagMask);
		}

		public static TEnum CommonFlags(TEnum value, TEnum flagMask)
		{
			ValidateIsValidFlagCombination(value, nameof(value));
			ValidateIsValidFlagCombination(flagMask, nameof(flagMask));
			return _and(value, flagMask);
		}

		public static TEnum CommonFlags(TEnum[] flags)
		{
			var flag = default(TEnum);
			var flagsLength = flags?.Length ?? 0;
			for (var i = 0; i < flagsLength; ++i)
			{
				var nextFlag = flags[i];
				ValidateIsValidFlagCombination(nextFlag, nameof(flags) + " must contain all valid flag combinations");
				flag = _and(flag, nextFlag);
			}
			return flag;
		}

		public static TEnum SetFlags(TEnum value, TEnum flagMask)
		{
			ValidateIsValidFlagCombination(value, nameof(value));
			ValidateIsValidFlagCombination(flagMask, nameof(flagMask));
			return _or(value, flagMask);
		}

		public static TEnum SetFlags(TEnum[] flags)
		{
			var flag = default(TEnum);
			var flagsLength = flags?.Length ?? 0;
			for (var i = 0; i < flagsLength; ++i)
			{
				var nextFlag = flags[i];
				ValidateIsValidFlagCombination(nextFlag, nameof(flags) + " must contain all valid flag combinations");
				flag = _or(flag, nextFlag);
			}
			return flag;
		}

		public static TEnum ClearFlags(TEnum value, TEnum flagMask)
		{
			ValidateIsValidFlagCombination(value, nameof(value));
			ValidateIsValidFlagCombination(flagMask, nameof(flagMask));
			return _and(value, _xor(flagMask, AllFlags));
		}
		#endregion

		#region Parsing
		public static TEnum ParseFlags(string value, EnumFormat[] parseFormatOrder) => ParseFlags(value, false, FlagEnums.DefaultDelimiter, parseFormatOrder);

		public static TEnum ParseFlags(string value, bool ignoreCase = false) => ParseFlags(value, ignoreCase, FlagEnums.DefaultDelimiter, null);

		public static TEnum ParseFlags(string value, bool ignoreCase, EnumFormat[] parseFormatOrder) => ParseFlags(value, ignoreCase, FlagEnums.DefaultDelimiter, parseFormatOrder);

		public static TEnum ParseFlags(string value, string delimiter) => ParseFlags(value, false, delimiter, null);

		public static TEnum ParseFlags(string value, string delimiter, EnumFormat[] parseFormatOrder) => ParseFlags(value, false, delimiter, parseFormatOrder);

		public static TEnum ParseFlags(string value, bool ignoreCase, string delimiter) => ParseFlags(value, ignoreCase, delimiter, null);

		public static TEnum ParseFlags(string value, bool ignoreCase, string delimiter, EnumFormat[] parseFormatOrder)
		{
			Preconditions.NotNull(value, nameof(value));
			Preconditions.NotNullOrEmpty(delimiter, nameof(delimiter));

			var effectiveDelimiter = delimiter.Trim();
			if (effectiveDelimiter.Length == 0)
			{
				effectiveDelimiter = delimiter;
			}
			var split = value.Split(new[] { effectiveDelimiter }, StringSplitOptions.None);

			if (!(parseFormatOrder?.Length > 0))
			{
				parseFormatOrder = new[] { EnumFormat.Name, EnumFormat.DecimalValue };
			}

			var result = default(TEnum);
			foreach (var indValue in split)
			{
				var trimmedIndValue = indValue.Trim();
				TEnum indValueAsTEnum;
				if (InternalTryParse(trimmedIndValue, ignoreCase, out indValueAsTEnum, parseFormatOrder))
				{
					if (!IsValidFlagCombination(indValueAsTEnum))
					{
						throw new ArgumentException("All individual enum values within value must be valid");
					}
					result = _or(result, indValueAsTEnum);
				}
				else
				{
					if (Enums.IsNumeric(indValue))
					{
						throw Enums.GetOverflowException();
					}
					throw new ArgumentException("value is not a valid combination of flag enum values");
				}
			}
			return result;
		}

		public static TEnum ParseFlagsOrDefault(string value, TEnum defaultEnum) => ParseFlagsOrDefault(value, false, FlagEnums.DefaultDelimiter, defaultEnum);

		public static TEnum ParseFlagsOrDefault(string value, bool ignoreCase, TEnum defaultEnum) => ParseFlagsOrDefault(value, ignoreCase, FlagEnums.DefaultDelimiter, defaultEnum);

		public static TEnum ParseFlagsOrDefault(string value, string delimiter, TEnum defaultEnum) => ParseFlagsOrDefault(value, false, delimiter, defaultEnum);

		public static TEnum ParseFlagsOrDefault(string value, bool ignoreCase, string delimiter, TEnum defaultEnum)
		{
			ValidateIsValidFlagCombination(defaultEnum, nameof(defaultEnum));

			TEnum enumValue;
			if (!TryParseFlags(value, ignoreCase, delimiter, out enumValue))
			{
				enumValue = defaultEnum;
			}
			return enumValue;
		}

		public static bool TryParseFlags(string value, out TEnum result) => TryParseFlags(value, false, FlagEnums.DefaultDelimiter, out result);

		public static bool TryParseFlags(string value, bool ignoreCase, out TEnum result) => TryParseFlags(value, ignoreCase, FlagEnums.DefaultDelimiter, out result);

		public static bool TryParseFlags(string value, string delimiter, out TEnum result) => TryParseFlags(value, false, delimiter, out result);

		public static bool TryParseFlags(string value, bool ignoreCase, string delimiter, out TEnum result, params EnumFormat[] parseFormatOrder)
		{
			Preconditions.NotNullOrEmpty(delimiter, nameof(delimiter));

			if (value == null)
			{
				result = default(TEnum);
				return false;
			}

			var effectiveDelimiter = delimiter.Trim();
			if (effectiveDelimiter.Length == 0)
			{
				effectiveDelimiter = delimiter;
			}
			var split = value.Split(new[] { effectiveDelimiter }, StringSplitOptions.None);

			if (!(parseFormatOrder?.Length > 0))
			{
				parseFormatOrder = new[] { EnumFormat.Name, EnumFormat.DecimalValue };
			}

			result = default(TEnum);
			foreach (var indValue in split)
			{
				var trimmedIndValue = indValue.Trim();
				TEnum indValueAsTEnum;
				if (!InternalTryParse(trimmedIndValue, ignoreCase, out indValueAsTEnum, parseFormatOrder) || !IsValidFlagCombination(indValueAsTEnum))
				{
					result = default(TEnum);
					return false;
				}
				result = _or(result, indValueAsTEnum);
			}
			return true;
		}
		#endregion

		#region Private Methods
		private static TEnum[] InternalGetFlags(TEnum value)
		{
			var valueAsULong = UnderlyingTypeCode == TypeCode.UInt64 ? (ulong)(object)value : (ulong)ToInt64(value);
			var values = new List<TEnum>();
			for (var currentValue = 1UL; currentValue <= valueAsULong && currentValue != 0UL; currentValue <<= 1)
			{
				var currentValueAsTEnum = InternalToEnum(currentValue);
				if (IsValidFlagCombination(currentValueAsTEnum) && InternalHasAnyFlags(value, currentValueAsTEnum))
				{
					values.Add(currentValueAsTEnum);
				}
			}
			return values.ToArray();
		}

		private static bool InternalHasAnyFlags(TEnum value, TEnum flagMask)
		{
			return !Equal(_and(value, flagMask), default(TEnum));
		}

		private static void ValidateIsValidFlagCombination(TEnum value, string paramName)
		{
			if (!IsValidFlagCombination(value))
			{
				throw new ArgumentException("must be valid flag combination", paramName);
			}
		}
		#endregion
		#endregion

		private EnumsCache() { }

		#region IEnumsCache Implementation
		#region Properties
		bool IEnumsCache.IsContiguous => IsContiguous;

		Type IEnumsCache.UnderlyingType => UnderlyingType;

		bool IEnumsCache.IsFlagEnum => IsFlagEnum;

		object IEnumsCache.AllFlags => AllFlags;
		#endregion

		#region Standard Enum Operations
		#region Type Methods
		int IEnumsCache.GetDefinedCount(bool uniqueValued) => GetDefinedCount(uniqueValued);

		string[] IEnumsCache.GetNames(bool uniqueValued) => GetNames(uniqueValued);

		object[] IEnumsCache.GetValues(bool uniqueValued) => GetValues(uniqueValued).Select(value => (object)value).ToArray();

		string[] IEnumsCache.GetDescriptions(bool uniqueValued) => GetDescriptions(uniqueValued);

		string[] IEnumsCache.GetDescriptionsOrNames(bool uniqueValued) => GetDescriptionsOrNames(uniqueValued);

		string[] IEnumsCache.GetDescriptionsOrNames(Func<string, string> nameFormatter, bool uniqueValued) => GetDescriptionsOrNames(nameFormatter, uniqueValued);

		string[] IEnumsCache.GetEnumMemberValues(bool uniqueValued) => GetEnumMemberValues(uniqueValued);

		Attribute[][] IEnumsCache.GetAllAttributes(bool uniqueValued) => GetAllAttributes(uniqueValued);

		int IEnumsCache.Compare(object x, object y) => Compare(ConvertToEnum(x, nameof(x)), ConvertToEnum(y, nameof(y)));
		#endregion

		#region IsValid
		bool IEnumsCache.IsValid(object value) => IsValid(value);

		bool IEnumsCache.IsValid(long value) => IsValid(value);

		bool IEnumsCache.IsValid(ulong value) => IsValid(value);
		#endregion

		#region IsDefined
		bool IEnumsCache.IsDefined(object value) => IsDefined(value);

		bool IEnumsCache.IsDefined(string name) => IsDefined(name);

		bool IEnumsCache.IsDefined(string name, bool ignoreCase) => IsDefined(name, ignoreCase);

		bool IEnumsCache.IsDefined(long value) => IsDefined(value);

		bool IEnumsCache.IsDefined(ulong value) => IsDefined(value);
		#endregion

		#region IsWithinUnderlyingTypesValueRange
		bool IEnumsCache.IsWithinUnderlyingTypesValueRange(long value) => IsWithinUnderlyingTypesValueRange(value);

		bool IEnumsCache.IsWithinUnderlyingTypesValueRange(ulong value) => IsWithinUnderlyingTypesValueRange(value);
		#endregion

		#region ToEnum
		object IEnumsCache.ToEnum(object value, bool validate) => ToEnum(value, validate);

		object IEnumsCache.ToEnum(long value, bool validate) => ToEnum(value, validate);

		object IEnumsCache.ToEnum(ulong value, bool validate) => ToEnum(value, validate);

		object IEnumsCache.ToEnumOrDefault(object value, object defaultEnum, bool validate)
		{
			object result;
			if (!((IEnumsCache)this).TryToEnum(value, out result, validate))
			{
				result = defaultEnum;
			}
			return result;
		}

		object IEnumsCache.ToEnumOrDefault(long value, object defaultEnum, bool validate)
		{
			object result;
			if (!((IEnumsCache)this).TryToEnum(value, out result, validate))
			{
				result = defaultEnum;
			}
			return result;
		}

		object IEnumsCache.ToEnumOrDefault(ulong value, object defaultEnum, bool validate)
		{
			object result;
			if (!((IEnumsCache)this).TryToEnum(value, out result, validate))
			{
				result = defaultEnum;
			}
			return result;
		}

		bool IEnumsCache.TryToEnum(object value, out object result, bool validate)
		{
			TEnum resultAsTEnum;
			var success = TryToEnum(value, out resultAsTEnum, validate);
			result = resultAsTEnum;
			return success;
		}

		bool IEnumsCache.TryToEnum(long value, out object result, bool validate)
		{
			TEnum resultAsTEnum;
			var success = TryToEnum(value, out resultAsTEnum, validate);
			result = resultAsTEnum;
			return success;
		}

		bool IEnumsCache.TryToEnum(ulong value, out object result, bool validate)
		{
			TEnum resultAsTEnum;
			var success = TryToEnum(value, out resultAsTEnum, validate);
			result = resultAsTEnum;
			return success;
		}
		#endregion

		#region Main Methods
		object IEnumsCache.Validate(object value, string paramName) => Validate(ConvertToEnum(value, nameof(value)), paramName);

		string IEnumsCache.GetName(object value) => GetName(ConvertToEnum(value, nameof(value)));

		string IEnumsCache.GetDescription(object value) => GetDescription(ConvertToEnum(value, nameof(value)));

		string IEnumsCache.GetDescription(string name) => GetDescription(name);

		string IEnumsCache.GetDescriptionOrName(object value) => GetDescriptionOrName(ConvertToEnum(value, nameof(value)));

		string IEnumsCache.GetDescriptionOrName(object value, Func<string, string> nameFormatter) => GetDescriptionOrName(ConvertToEnum(value, nameof(value)), nameFormatter);

		string IEnumsCache.GetDescriptionOrName(string name) => GetDescriptionOrName(name);

		string IEnumsCache.GetDescriptionOrName(string name, Func<string, string> nameFormatter) => GetDescriptionOrName(name, nameFormatter);

		string IEnumsCache.GetEnumMemberValue(object value) => GetEnumMemberValue(ConvertToEnum(value, nameof(value)));

		string IEnumsCache.GetEnumMemberValue(string name) => GetEnumMemberValue(name);

		string IEnumsCache.AsString(object value) => AsString(ConvertToEnum(value, nameof(value)));

		string IEnumsCache.AsString(object value, string format) => AsString(ConvertToEnum(value, nameof(value)), format);

		string IEnumsCache.AsString(object value, params EnumFormat[] formats) => AsString(ConvertToEnum(value, nameof(value)), formats);

		string IEnumsCache.AsString(string name) => AsString(name);

		string IEnumsCache.AsString(string name, string format) => AsString(name, format);

		string IEnumsCache.AsString(string name, params EnumFormat[] formats) => AsString(name, formats);

		string IEnumsCache.Format(object value, string format) => Format(ConvertToEnum(value, nameof(value)), format);

		string IEnumsCache.Format(object value, params EnumFormat[] formats) => Format(ConvertToEnum(value, nameof(value)), formats);

		string IEnumsCache.Format(string name, string format) => Format(name, format);

		string IEnumsCache.Format(string name, params EnumFormat[] formats) => Format(name, formats);

		object IEnumsCache.GetUnderlyingValue(object value) => GetUnderlyingValue(ConvertToEnum(value, nameof(value)));

		sbyte IEnumsCache.ToSByte(object value) => ToSByte(ConvertToEnum(value, nameof(value)));

		byte IEnumsCache.ToByte(object value) => ToByte(ConvertToEnum(value, nameof(value)));

		short IEnumsCache.ToInt16(object value) => ToInt16(ConvertToEnum(value, nameof(value)));

		ushort IEnumsCache.ToUInt16(object value) => ToUInt16(ConvertToEnum(value, nameof(value)));

		int IEnumsCache.ToInt32(object value) => ToInt32(ConvertToEnum(value, nameof(value)));

		uint IEnumsCache.ToUInt32(object value) => ToUInt32(ConvertToEnum(value, nameof(value)));

		long IEnumsCache.ToInt64(object value) => ToInt64(ConvertToEnum(value, nameof(value)));

		ulong IEnumsCache.ToUInt64(object value) => ToUInt64(ConvertToEnum(value, nameof(value)));
		#endregion

		#region Attributes
		Attribute[] IEnumsCache.GetAllAttributes(object value) => GetAllAttributes(ConvertToEnum(value, nameof(value)));

		Attribute[] IEnumsCache.GetAllAttributes(string name) => GetAllAttributes(name);
		#endregion

		#region Parsing
		object IEnumsCache.Parse(string value) => Parse(value);

		object IEnumsCache.Parse(string value, bool ignoreCase) => Parse(value, ignoreCase);

		object IEnumsCache.Parse(string value, params EnumFormat[] parseOrder) => Parse(value, parseOrder);

		object IEnumsCache.Parse(string value, bool ignoreCase, params EnumFormat[] parseOrder) => Parse(value, ignoreCase, parseOrder);

		object IEnumsCache.ParseOrDefault(string value, object defaultEnum)
		{
			object result;
			if (!((IEnumsCache)this).TryParse(value, out result))
			{
				result = defaultEnum;
			}
			return result;
		}

		object IEnumsCache.ParseOrDefault(string value, bool ignoreCase, object defaultEnum)
		{
			object result;
			if (!((IEnumsCache)this).TryParse(value, ignoreCase, out result))
			{
				result = defaultEnum;
			}
			return result;
		}

		object IEnumsCache.ParseOrDefault(string value, object defaultEnum, params EnumFormat[] parseOrder)
		{
			object result;
			if (!((IEnumsCache)this).TryParse(value, out result, parseOrder))
			{
				result = defaultEnum;
			}
			return result;
		}

		object IEnumsCache.ParseOrDefault(string value, bool ignoreCase, object defaultEnum, params EnumFormat[] parseOrder)
		{
			object result;
			if (!((IEnumsCache)this).TryParse(value, ignoreCase, out result, parseOrder))
			{
				result = defaultEnum;
			}
			return result;
		}

		bool IEnumsCache.TryParse(string value, out object result)
		{
			TEnum resultAsTEnum;
			var success = TryParse(value, out resultAsTEnum);
			result = resultAsTEnum;
			return success;
		}

		bool IEnumsCache.TryParse(string value, bool ignoreCase, out object result)
		{
			TEnum resultAsTEnum;
			var success = TryParse(value, ignoreCase, out resultAsTEnum);
			result = resultAsTEnum;
			return success;
		}

		bool IEnumsCache.TryParse(string value, out object result, params EnumFormat[] parseOrder)
		{
			TEnum resultAsTEnum;
			var success = TryParse(value, out resultAsTEnum, parseOrder);
			result = resultAsTEnum;
			return success;
		}

		bool IEnumsCache.TryParse(string value, bool ignoreCase, out object result, params EnumFormat[] parseOrder)
		{
			TEnum resultAsTEnum;
			var success = TryParse(value, ignoreCase, out resultAsTEnum, parseOrder);
			result = resultAsTEnum;
			return success;
		}
		#endregion
		#endregion

		#region Flag Enum Operations
		#region Main Methods
		bool IEnumsCache.IsValidFlagCombination(object value) => IsValidFlagCombination(ConvertToEnum(value, nameof(value)));

		string IEnumsCache.FormatAsFlags(object value) => FormatAsFlags(ConvertToEnum(value, nameof(value)));

		string IEnumsCache.FormatAsFlags(object value, string delimiter) => FormatAsFlags(ConvertToEnum(value, nameof(value)), delimiter);

		string IEnumsCache.FormatAsFlags(object value, EnumFormat[] formats) => FormatAsFlags(ConvertToEnum(value, nameof(value)), formats);

		string IEnumsCache.FormatAsFlags(object value, string delimiter, EnumFormat[] formats) => FormatAsFlags(ConvertToEnum(value, nameof(value)), delimiter, formats);

		object[] IEnumsCache.GetFlags(object value) => GetFlags(ConvertToEnum(value, nameof(value))).Select(flag => (object)flag).ToArray();

		bool IEnumsCache.HasAnyFlags(object value) => HasAnyFlags(ConvertToEnum(value, nameof(value)));

		bool IEnumsCache.HasAnyFlags(object value, object flagMask) => HasAnyFlags(ConvertToEnum(value, nameof(value)), ConvertToEnum(flagMask, nameof(flagMask)));

		bool IEnumsCache.HasAllFlags(object value) => HasAllFlags(ConvertToEnum(value, nameof(value)));

		bool IEnumsCache.HasAllFlags(object value, object flagMask) => HasAllFlags(ConvertToEnum(value, nameof(value)), ConvertToEnum(flagMask, nameof(flagMask)));

		object IEnumsCache.InvertFlags(object value) => InvertFlags(ConvertToEnum(value, nameof(value)));

		object IEnumsCache.InvertFlags(object value, object flagMask) => InvertFlags(ConvertToEnum(value, nameof(value)), ConvertToEnum(flagMask, nameof(flagMask)));

		object IEnumsCache.CommonFlags(object value, object flagMask) => CommonFlags(ConvertToEnum(value, nameof(value)), ConvertToEnum(flagMask, nameof(flagMask)));

		object IEnumsCache.CommonFlags(object[] flags) => CommonFlags(ConvertToEnumArray(flags, nameof(flags)));

		object IEnumsCache.SetFlags(object value, object flagMask) => SetFlags(ConvertToEnum(value, nameof(value)), ConvertToEnum(flagMask, nameof(flagMask)));

		object IEnumsCache.SetFlags(object[] flags) => SetFlags(ConvertToEnumArray(flags, nameof(flags)));

		object IEnumsCache.ClearFlags(object value, object flagMask) => ClearFlags(ConvertToEnum(value, nameof(value)), ConvertToEnum(flagMask, nameof(flagMask)));
		#endregion

		#region Parsing
		object IEnumsCache.ParseFlags(string value) => ParseFlags(value);

		object IEnumsCache.ParseFlags(string value, bool ignoreCase) => ParseFlags(value, ignoreCase);

		object IEnumsCache.ParseFlags(string value, string delimiter) => ParseFlags(value, delimiter);

		object IEnumsCache.ParseFlags(string value, bool ignoreCase, string delimiter) => ParseFlags(value, ignoreCase, delimiter);

		object IEnumsCache.ParseFlagsOrDefault(string value, object defaultEnum)
		{
			object result;
			if (!((IEnumsCache)this).TryParseFlags(value, out result))
			{
				result = defaultEnum;
			}
			return result;
		}

		object IEnumsCache.ParseFlagsOrDefault(string value, bool ignoreCase, object defaultEnum)
		{
			object result;
			if (!((IEnumsCache)this).TryParseFlags(value, ignoreCase, out result))
			{
				result = defaultEnum;
			}
			return result;
		}

		object IEnumsCache.ParseFlagsOrDefault(string value, string delimiter, object defaultEnum)
		{
			object result;
			if (!((IEnumsCache)this).TryParseFlags(value, delimiter, out result))
			{
				result = defaultEnum;
			}
			return result;
		}

		object IEnumsCache.ParseFlagsOrDefault(string value, bool ignoreCase, string delimiter, object defaultEnum)
		{
			object result;
			if (!((IEnumsCache)this).TryParseFlags(value, ignoreCase, delimiter, out result))
			{
				result = defaultEnum;
			}
			return result;
		}

		bool IEnumsCache.TryParseFlags(string value, out object result)
		{
			TEnum resultAtTEnum;
			var success = TryParseFlags(value, out resultAtTEnum);
			result = resultAtTEnum;
			return success;
		}

		bool IEnumsCache.TryParseFlags(string value, bool ignoreCase, out object result)
		{
			TEnum resultAtTEnum;
			var success = TryParseFlags(value, ignoreCase, out resultAtTEnum);
			result = resultAtTEnum;
			return success;
		}

		bool IEnumsCache.TryParseFlags(string value, string delimiter, out object result)
		{
			TEnum resultAtTEnum;
			var success = TryParseFlags(value, delimiter, out resultAtTEnum);
			result = resultAtTEnum;
			return success;
		}

		bool IEnumsCache.TryParseFlags(string value, bool ignoreCase, string delimiter, out object result)
		{
			TEnum resultAtTEnum;
			var success = TryParseFlags(value, ignoreCase, delimiter, out resultAtTEnum);
			result = resultAtTEnum;
			return success;
		}
		#endregion
		#endregion

		#region Private Methods
		private static TEnum ConvertToEnum(object value, string paramName)
		{
			TEnum result;
			if (TryToEnum(value, out result, false))
			{
				return result;
			}
			throw new ArgumentException($"value is not of type {typeof(TEnum).Name}", paramName);
		}

		private static TEnum[] ConvertToEnumArray(object[] values, string paramName)
		{
			TEnum[] enumValues = null;
			if (values != null)
			{
				enumValues = new TEnum[values.Length];
				for (var i = 0; i < values.Length; ++i)
				{
					enumValues[i] = ConvertToEnum(values[i], $"{paramName}[{i}]");
				}
			}
			return enumValues;
		}
		#endregion
		#endregion
	}
}