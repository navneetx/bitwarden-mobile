﻿using System.Collections.Generic;
using Android.Service.Autofill;
using Android.Views.Autofill;
using System.Linq;
using Android.Text;
using Android.Views;

namespace Bit.Android.Autofill
{
    public class FieldCollection
    {
        private List<Field> _passwordFields = null;
        private List<Field> _usernameFields = null;

        public HashSet<int> Ids { get; private set; } = new HashSet<int>();
        public List<AutofillId> AutofillIds { get; private set; } = new List<AutofillId>();
        public SaveDataType SaveType
        {
            get
            {
                if(FillableForLogin)
                {
                    return SaveDataType.Password;
                }
                else if(FillableForCard)
                {
                    return SaveDataType.CreditCard;
                }

                return SaveDataType.Generic;
            }
        }
        public HashSet<string> Hints { get; private set; } = new HashSet<string>();
        public HashSet<string> FocusedHints { get; private set; } = new HashSet<string>();
        public List<Field> Fields { get; private set; } = new List<Field>();
        public IDictionary<int, Field> IdToFieldMap { get; private set; } =
            new Dictionary<int, Field>();
        public IDictionary<string, List<Field>> HintToFieldsMap { get; private set; } =
            new Dictionary<string, List<Field>>();
        public List<AutofillId> IgnoreAutofillIds { get; private set; } = new List<AutofillId>();

        public List<Field> PasswordFields
        {
            get
            {
                if(_passwordFields != null)
                {
                    return _passwordFields;
                }

                if(Hints.Any())
                {
                    _passwordFields = new List<Field>();
                    if(HintToFieldsMap.ContainsKey(View.AutofillHintPassword))
                    {
                        _passwordFields.AddRange(HintToFieldsMap[View.AutofillHintPassword]);
                    }
                }
                else
                {
                    _passwordFields = Fields
                        .Where(f => 
                            !f.IdEntry.ToLowerInvariant().Contains("search") &&
                            (!f.Node.Hint?.ToLowerInvariant().Contains("search") ?? true) &&
                            (
                                f.InputType.HasFlag(InputTypes.TextVariationPassword) ||
                                f.InputType.HasFlag(InputTypes.TextVariationVisiblePassword) ||
                                f.InputType.HasFlag(InputTypes.TextVariationWebPassword)
                            )
                        ).ToList();
                    if(!_passwordFields.Any())
                    {
                        _passwordFields = Fields.Where(f => f.IdEntry?.ToLower().Contains("password") ?? false).ToList();
                    }
                }

                return _passwordFields;
            }
        }

        public List<Field> UsernameFields
        {
            get
            {
                if(_usernameFields != null)
                {
                    return _usernameFields;
                }

                _usernameFields = new List<Field>();
                if(Hints.Any())
                {
                    if(HintToFieldsMap.ContainsKey(View.AutofillHintEmailAddress))
                    {
                        _usernameFields.AddRange(HintToFieldsMap[View.AutofillHintEmailAddress]);
                    }
                    if(HintToFieldsMap.ContainsKey(View.AutofillHintUsername))
                    {
                        _usernameFields.AddRange(HintToFieldsMap[View.AutofillHintUsername]);
                    }
                }
                else
                {
                    foreach(var passwordField in PasswordFields)
                    {
                        var usernameField = Fields.TakeWhile(f => f.Id != passwordField.Id).LastOrDefault();
                        if(usernameField != null)
                        {
                            _usernameFields.Add(usernameField);
                        }
                    }
                }

                return _usernameFields;
            }
        }

        public bool FillableForLogin => FocusedHintsContain(
            new string[] { View.AutofillHintUsername, View.AutofillHintEmailAddress, View.AutofillHintPassword }) ||
            UsernameFields.Any(f => f.Focused) || PasswordFields.Any(f => f.Focused);
        public bool FillableForCard => FocusedHintsContain(
            new string[] { View.AutofillHintCreditCardNumber, View.AutofillHintCreditCardExpirationMonth,
                View.AutofillHintCreditCardExpirationYear, View.AutofillHintCreditCardSecurityCode});
        public bool FillableForIdentity => FocusedHintsContain(
            new string[] { View.AutofillHintName, View.AutofillHintPhone, View.AutofillHintPostalAddress,
                View.AutofillHintPostalCode });

        public bool Fillable => FillableForLogin || FillableForCard || FillableForIdentity;

        public void Add(Field field)
        {
            if(Ids.Contains(field.Id))
            {
                return;
            }

            _passwordFields = _usernameFields = null;

            Ids.Add(field.Id);
            Fields.Add(field);
            AutofillIds.Add(field.AutofillId);
            IdToFieldMap.Add(field.Id, field);

            if(field.Hints != null)
            {
                foreach(var hint in field.Hints)
                {
                    Hints.Add(hint);
                    if(field.Focused)
                    {
                        FocusedHints.Add(hint);
                    }

                    if(!HintToFieldsMap.ContainsKey(hint))
                    {
                        HintToFieldsMap.Add(hint, new List<Field>());
                    }

                    HintToFieldsMap[hint].Add(field);
                }
            }
        }

        public SavedItem GetSavedItem()
        {
            if(SaveType == SaveDataType.Password)
            {
                var passwordField = PasswordFields.FirstOrDefault(f => !string.IsNullOrWhiteSpace(f.TextValue));
                if(passwordField == null)
                {
                    return null;
                }

                var savedItem = new SavedItem
                {
                    Type = App.Enums.CipherType.Login,
                    Login = new SavedItem.LoginItem
                    {
                        Password = GetFieldValue(passwordField)
                    }
                };

                var usernameField = Fields.TakeWhile(f => f.Id != passwordField.Id).LastOrDefault();
                savedItem.Login.Username = GetFieldValue(usernameField);

                return savedItem;
            }
            else if(SaveType == SaveDataType.CreditCard)
            {
                var savedItem = new SavedItem
                {
                    Type = App.Enums.CipherType.Card,
                    Card = new SavedItem.CardItem
                    {
                        Number = GetFieldValue(View.AutofillHintCreditCardNumber),
                        Name = GetFieldValue(View.AutofillHintName),
                        ExpMonth = GetFieldValue(View.AutofillHintCreditCardExpirationMonth, true),
                        ExpYear = GetFieldValue(View.AutofillHintCreditCardExpirationYear),
                        Code = GetFieldValue(View.AutofillHintCreditCardSecurityCode)
                    }
                };

                return savedItem;
            }

            return null;
        }

        public AutofillId[] GetOptionalSaveIds()
        {
            if(SaveType == SaveDataType.Password)
            {
                return UsernameFields.Select(f => f.AutofillId).ToArray();
            }
            else if(SaveType == SaveDataType.CreditCard)
            {
                var fieldList = new List<Field>();
                if(HintToFieldsMap.ContainsKey(View.AutofillHintCreditCardSecurityCode))
                {
                    fieldList.AddRange(HintToFieldsMap[View.AutofillHintCreditCardSecurityCode]);
                }
                if(HintToFieldsMap.ContainsKey(View.AutofillHintCreditCardExpirationYear))
                {
                    fieldList.AddRange(HintToFieldsMap[View.AutofillHintCreditCardExpirationYear]);
                }
                if(HintToFieldsMap.ContainsKey(View.AutofillHintCreditCardExpirationMonth))
                {
                    fieldList.AddRange(HintToFieldsMap[View.AutofillHintCreditCardExpirationMonth]);
                }
                if(HintToFieldsMap.ContainsKey(View.AutofillHintName))
                {
                    fieldList.AddRange(HintToFieldsMap[View.AutofillHintName]);
                }
                return fieldList.Select(f => f.AutofillId).ToArray();
            }

            return new AutofillId[0];
        }

        public AutofillId[] GetRequiredSaveFields()
        {
            if(SaveType == SaveDataType.Password)
            {
                return PasswordFields.Select(f => f.AutofillId).ToArray();
            }
            else if(SaveType == SaveDataType.CreditCard && HintToFieldsMap.ContainsKey(View.AutofillHintCreditCardNumber))
            {
                return HintToFieldsMap[View.AutofillHintCreditCardNumber].Select(f => f.AutofillId).ToArray();
            }

            return new AutofillId[0];
        }

        private bool FocusedHintsContain(IEnumerable<string> hints)
        {
            return hints.Any(h => FocusedHints.Contains(h));
        }

        private string GetFieldValue(string hint, bool monthValue = false)
        {
            if(HintToFieldsMap.ContainsKey(hint))
            {
                foreach(var field in HintToFieldsMap[hint])
                {
                    var val = GetFieldValue(field, monthValue);
                    if(!string.IsNullOrWhiteSpace(val))
                    {
                        return val;
                    }
                }
            }

            return null;
        }

        private string GetFieldValue(Field field, bool monthValue = false)
        {
            if(field == null)
            {
                return null;
            }

            if(!string.IsNullOrWhiteSpace(field.TextValue))
            {
                if(field.AutofillType == AutofillType.List && field.ListValue.HasValue && monthValue)
                {
                    if(field.AutofillOptions.Count == 13)
                    {
                        return field.ListValue.ToString();
                    }
                    else if(field.AutofillOptions.Count == 12)
                    {
                        return (field.ListValue + 1).ToString();
                    }
                }
                return field.TextValue;
            }
            else if(field.DateValue.HasValue)
            {
                return field.DateValue.Value.ToString();
            }
            else if(field.ToggleValue.HasValue)
            {
                return field.ToggleValue.Value.ToString();
            }

            return null;
        }
    }
}