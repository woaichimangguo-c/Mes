﻿using Mes.Client.Model.Constants;
using Mes.Client.Model.Parm;
using Mes.Client.Service;
using Mes.Client.Service.BE;
using Mes.Client.Utility;
using Mes.Client.Utility.Pages;
using MES.Libraries;
using System;
using System.Collections.Generic;
using System.Data;
using System.Linq;
using System.Reflection;
using System.Web;

namespace Mes.Client.UI.Ashx
{
    /// <summary>
    /// ProductBOMHandler 的摘要说明
    /// </summary>
    public class ProductBOMHandler : BaseHttpHandler
    {
        #region ===================初始加载=====================
        ProductBOMParm parm;
        public override void ProcessRequest(HttpContext context)
        {
            try
            {
                base.ProcessRequest(context);
                string method = Request["Method"] ?? "";

                if (!string.IsNullOrEmpty(method))
                {
                    parm = new ProductBOMParm(context);
                    foreach (MethodInfo mi in this.GetType().GetMethods())
                    {
                        if (mi.Name.ToLower() == method.ToLower())
                        {
                            mi.Invoke(this, null);
                            break;
                        }
                    }
                }
            }
            catch (Exception ex)
            {
                Response.Write(ex);
            }
        }
        #endregion

        public void List()
        {
            try
            {
                using (ProxyBE p = new ProxyBE())
                {
                    SearchProductBOMArgs args = new SearchProductBOMArgs();
                    if (!string.IsNullOrEmpty(parm.BOMID))
                    {
                        args.BOMID = parm.BOMID;
                    }
                    if (!string.IsNullOrEmpty(parm.ProductCode))
                    {
                        args.ProductCode = parm.ProductCode;
                    }
                    if (!string.IsNullOrEmpty(parm.ProductName))
                    {
                        args.ProductName = parm.ProductName;
                    }
                    if (!string.IsNullOrEmpty(Request["Status"]))
                    {
                        args.Status = Convert.ToBoolean(Request["Status"]);
                    }
                    if (!string.IsNullOrEmpty(Request["CreatedFrom"]))
                    {
                        args.CreatedFrom = parm.CreatedFrom;
                    }
                    if (!string.IsNullOrEmpty(Request["CreatedTo"]))
                    {
                        args.CreatedTo = parm.CreatedTo.AddDays(1);
                    }
                    args.OrderBy = string.IsNullOrEmpty(pagingParm.SortOrder.Trim()) ? "ID" : pagingParm.SortOrder;
                    args.RowNumberFrom = pagingParm.RowNumberFrom;
                    args.RowNumberTo = pagingParm.RowNumberTo;
                    //Where

                    SearchResult sr = p.Client.SearchProductBOM(SenderUser, args);
                    Response.Write(JSONHelper.Dataset2Json(sr.DataSet));
                }
            }
            catch (Exception ex)
            {
                WriteError(ex.Message, ex);
            }
        }

        /// <summary>
        /// 导入BOM文件
        /// </summary>
        public void ImportBOM()
        {
            try
            {
                using (var p = new ProxyBE())
                {
                    string bomID = Request["BOMID"];
                    string productID = Request["ProductCode"];
                    string filePath = Request["FilePath"];
                    if (string.IsNullOrEmpty(bomID) || string.IsNullOrEmpty(productID))
                    {
                        throw new Exception("BOMID和产品编号为空或者不存在");
                    }
                    if (p.Client.GetProductComponentByProductCode(SenderUser, productID).Count > 0) //检验产品是否已经导入
                    {
                        throw new Exception("该产品的BOM已经导入，请更换其他产品");
                    }
                    if (string.IsNullOrEmpty(filePath))
                    {
                        throw new Exception("要导入的BOM文件路径错误，请检查后重新上传");
                    }
                    DataTable table = NPOIHelper.ImportExceltoDt(Server.MapPath(filePath));
                    if (table.Rows.Count <= 0)
                    {
                        throw new Exception("BOM文件没有相应数据，不能为空");
                    }

                    string[] componentTypeLevel = { "第一阶层", "第二阶层", "第三阶层" };
                    List<ComponentType> componentTypeList = GetComponentTypeList(); //获取数据库表中所有组件类型列表
                    List<DataRow> lstAllRow = table.AsEnumerable().Where(x => x.Field<string>("产品ID").ToString().Equals(productID)).ToList(); //获取BOM表中该产品所有行
                    if (lstAllRow.Count <= 0)
                    {
                        throw new Exception("BOM文件中的产品ID与要导入的产品编号不一致");
                    }

                    List<string> lstFistType = lstAllRow.Select(x => x.Field<string>(componentTypeLevel[0])).Where(x => !string.IsNullOrEmpty(x)).Distinct().ToList(); //获取BOM表中第一阶层的组件类型（去重）

                    SaveProductComponentArgs args = new SaveProductComponentArgs();
                    args.ProductComponents = new List<ProductComponent>();

                    foreach (string firstTypeName in lstFistType)
                    {
                        List<DataRow> lstFirstTypeRow = lstAllRow.Where(x => x.Field<string>(componentTypeLevel[0]).ToString().Equals(firstTypeName)).ToList();

                        //循环取出第一阶层下面第二、三阶层的所有行
                        for (int i = 0; i < componentTypeLevel.Length; i++)
                        {
                            List<string> lstChildType = lstFirstTypeRow.Select(x => x.Field<string>(componentTypeLevel[i])).Where(x => !string.IsNullOrEmpty(x)).Distinct().ToList(); //获取第一阶层下面第二、三阶层的组件类型（去重）
                            foreach (string childTypeName in lstChildType)
                            {
                                //首先确认BOM表中组件类型在数据库表中是存在并且有效的
                                var firstComponentType = componentTypeList.FirstOrDefault(x => x.ComponentTypeName.Equals(childTypeName));
                                if (firstComponentType != null)
                                {
                                    //每一阶层，每种组件类型所有的行
                                    List<DataRow> lstChildTypeRow = GetDataRowListBy(componentTypeLevel[i], firstComponentType.ComponentTypeName, lstFirstTypeRow);

                                    ProductComponent productComponent = new ProductComponent();
                                    productComponent.ComponentCode = productID + "-" + firstComponentType.ComponentTypeCode;
                                    productComponent.ProductCode = productID;
                                    productComponent.ComponentTypeID = firstComponentType.ComponentTypeID;
                                    productComponent.ComponentTypeName = firstComponentType.ComponentTypeName;
                                    productComponent.Quantity = lstChildTypeRow.Count;
                                    productComponent.Amount = lstChildTypeRow.Select(x => x.Field<string>("用量")).Where(x => !string.IsNullOrEmpty(x)).Sum(x => Convert.ToDecimal(x));
                                    args.ProductComponents.Add(productComponent);
                                }
                            }
                        }
                    }

                    p.Client.SaveProductComponents(SenderUser, args); //Insert ProductComponent

                    List<ComponentMaterial> lstComponentMaterial = new List<ComponentMaterial>();
                    List<ProductComponent> lstProductComponent = p.Client.GetProductComponentByProductCode(SenderUser, productID);
                    LoadComponentMaterialList(lstAllRow, componentTypeLevel.ToList(), lstProductComponent, ref lstComponentMaterial);
                    SaveComponentMaterialArgs componentMaterialArgs = new SaveComponentMaterialArgs();
                    componentMaterialArgs.ComponentMaterials = lstComponentMaterial;
                    p.Client.SaveComponentMaterialAndExtension(SenderUser, componentMaterialArgs); //Insert ComponentMaterial

                    p.Client.UpdateProductBOMStatusByBOMID(SenderUser, new ProductBOM() { BOMID = bomID, Status = true }); //Update ProductBOM Status更新状态为已上传

                    WriteJsonSuccess("导入成功");
                }
            }
            catch (Exception ex)
            {
                WriteJsonError(ex.Message);
            }
        }

        /// <summary>
        /// 加载组件物料数据，ComponentMaterialExtension
        /// </summary>
        /// <param name="lstAllRow"></param>
        /// <param name="componentTypeLevel"></param>
        /// <param name="lstProductComponent"></param>
        protected void LoadComponentMaterialList(List<DataRow> lstAllRow, List<string> componentTypeLevel, List<ProductComponent> lstProductComponent, ref List<ComponentMaterial> lstComponentMaterial)
        {
            //当前是第几阶层
            string currentTypeLevel = componentTypeLevel[componentTypeLevel.Count - 1];
            //获取每一阶层下所有的组件类型
            List<string> lstChildType = lstAllRow.Select(x => x.Field<string>(currentTypeLevel)).Distinct().ToList();
            Dictionary<string, List<DataRow>> dicTypeListRow = lstAllRow.GroupBy(x => x.Field<string>(currentTypeLevel)).ToDictionary(group => group.Key, group => group.ToList());
            if (dicTypeListRow == null || dicTypeListRow.Count == 0) return;
            foreach (KeyValuePair<string, List<DataRow>> kvp in dicTypeListRow)
            {
                //如果阶层的组件类型名称为空,则继续循环下一阶层
                if (string.IsNullOrEmpty(kvp.Key))
                {
                    componentTypeLevel.Remove(currentTypeLevel);
                    LoadComponentMaterialList(kvp.Value, componentTypeLevel, lstProductComponent, ref lstComponentMaterial);
                }
                else
                {
                    //确保该组件类型在数据表ProductComponent中是存在并且有效的，防止错误数据导入
                    var productComponent = lstProductComponent.FirstOrDefault(x => x.ComponentTypeName.Equals(kvp.Key));
                    if (productComponent == null) continue;
                    foreach (DataRow dr in kvp.Value)
                    {
                        ComponentMaterial model = new ComponentMaterial()
                        {
                            ComponentID = productComponent.ComponentID,
                            MaterialCode = dr["材料编码"].ToString(),
                            MaterialName = dr["材料名称"].ToString(),
                            Specification = dr["材料规格"].ToString(),
                            Unit = dr["单位"].ToString(),
                            Amount = dr["用量"] == null ? 0 : Convert.ToDecimal(dr["用量"]),
                            Quantity = dr["数量"] == null ? 0 : Convert.ToDecimal(dr["数量"]),
                            PlateName = dr["工件名称"].ToString(),
                            Material = dr["材料"].ToString(),
                            //Color = dr["颜色"].ToString(),
                            Length = dr["长"].ToString(),
                            Width = dr["宽"].ToString(),
                            Height = dr["厚"].ToString(),
                            CutLength = dr["开料长"].ToString(),
                            CutWidth = dr["开料宽"].ToString(),
                            //CutHeight = dr["开料厚"].ToString(),
                            CutArea = dr["开料面积"].ToString(),
                            EdgeFront = dr["前封边"].ToString(),
                            EdgeBack = dr["后封边"].ToString(),
                            EdgeLeft = dr["左封边"].ToString(),
                            EdgeRight = dr["右封边"].ToString(),
                            Veins = dr["纹路"].ToString(),
                            Routing = dr["工艺路线"].ToString(),
                            IsOptimization = dr["是否需要优化"] == null ? false : (dr["是否需要优化"].ToString() == "1" ? true : false),
                            Status = false,
                            ExtensionModel = new ComponentMaterialExtension()
                            {
                                Barcode = dr["条形码"].ToString(),
                                OutputName = dr["输出名称"].ToString(),
                                MprA = dr["A"].ToString(),
                                MprB = dr["B"].ToString(),
                                MachineFile = dr["加工程序"].ToString(),
                                Remark = dr["工件备注"].ToString()
                            }
                        };
                        lstComponentMaterial.Add(model);
                    }

                }
            }
        }

        #region 私有方法
        /// <summary>
        /// 获取所有组件类型列表
        /// </summary>
        /// <returns></returns>
        private List<ComponentType> GetComponentTypeList()
        {
            string cacheKey = "Key_ComponentType"; //缓存Key
            int expires = 30; //缓存过期时间（分钟）
            List<ComponentType> lstComponentType = CacheHelper.Get<List<ComponentType>>(cacheKey);
            if (lstComponentType == null || lstComponentType.Count == 0)
            {
                using (var p = new ProxyBE())
                {
                    List<ComponentType> lstAllComponentType = p.Client.GetAllComponentTypes(SenderUser);
                    if (lstAllComponentType != null && lstAllComponentType.Count > 0)
                    {
                        lstComponentType = lstAllComponentType.Where(x => x.Status == false).ToList();
                        CacheHelper.Insert(cacheKey, lstComponentType, expires);
                    }
                }
            }
            return lstComponentType;
        }

        private List<DataRow> GetDataRowListBy(string columnName, string componentTypeName, List<DataRow> lstAllRow)
        {
            var childRows = lstAllRow.Where(x => x.Field<string>(columnName).Equals(componentTypeName));
            if (childRows != null)
            {
                return childRows.ToList();
            }
            return null;
        }

        private List<ComponentType> GetDataRowBy(int componentTypeID, List<ComponentType> componentTypeList)
        {
            List<ComponentType> childTypes = componentTypeList.Where(x => x.ParentID == componentTypeID).ToList();
            if (childTypes.Count > 0)
            {
                foreach (var subItem in childTypes)
                {
                    return GetDataRowBy(subItem.ComponentTypeID, componentTypeList);
                }
            }
            return null;
        }

        private bool IsHasChildLevel()
        {
            return true;
        }
        #endregion
    }
}