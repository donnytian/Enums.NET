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

using System.Collections;
using System.Collections.Generic;

namespace EnumsNET.Unsafe
{
	/// <summary>
	/// An efficient type-unsafe enum comparer which doesn't box the values for use with generics
	/// </summary>
	/// <typeparam name="TEnum"></typeparam>
	public class UnsafeEnumComparer<TEnum> : IEqualityComparer<TEnum>, IComparer<TEnum>, IEqualityComparer, IComparer
	{
		/// <summary>
		/// Constructor for an efficient type-unsafe enum comparer which doesn't box the values for use with generics
		/// </summary>
		public UnsafeEnumComparer()
		{
			UnsafeEnums.VerifyTypeIsEnum<TEnum>();
		}

		/// <summary>
		/// Indicates if <paramref name="x"/> equals <paramref name="y"/> without boxing the values.
		/// </summary>
		/// <param name="x"></param>
		/// <param name="y"></param>
		/// <returns>Indication if <paramref name="x"/> equals <paramref name="y"/> without boxing the values.</returns>
		public bool Equals(TEnum x, TEnum y) => EnumsCache<TEnum>.Equal(x, y);

		/// <summary>
		/// Retrieves a hash code for <paramref name="obj"/> without boxing the value.
		/// </summary>
		/// <param name="obj"></param>
		/// <returns>Hash code for <paramref name="obj"/> without boxing the value.</returns>
		public int GetHashCode(TEnum obj) => EnumsCache<TEnum>.GetHashCode(obj);

		/// <summary>
		/// Compares <paramref name="x"/> to <paramref name="y"/> without boxing the values.
		/// </summary>
		/// <param name="x"></param>
		/// <param name="y"></param>
		/// <returns></returns>
		public int Compare(TEnum x, TEnum y) => EnumsCache<TEnum>.Compare(x, y);

		#region Explicit Interface Implementation
		bool IEqualityComparer.Equals(object x, object y)
		{
			if (x is TEnum && y is TEnum)
			{
				return Equals((TEnum)x, (TEnum)y);
			}
			return false;
		}

		int IEqualityComparer.GetHashCode(object obj)
		{
			if (obj is TEnum)
			{
				return GetHashCode((TEnum)obj);
			}
			return 0;
		}

		int IComparer.Compare(object x, object y)
		{
			if (x is TEnum && y is TEnum)
			{
				return Compare((TEnum)x, (TEnum)y);
			}
			return 0;
		}
		#endregion
	}
}